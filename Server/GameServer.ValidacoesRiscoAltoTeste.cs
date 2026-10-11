using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Ranks;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DAS VALIDACOES, SEGUNDA RODADA (`--validacoesteste`, secoes 10 a 17).
///
/// O dono (2026-10-11), sobre a lista de risco alto que a primeira rodada so listou: *"resolva todos esses
/// problemas, e o guardiao tem q estar no templo pra soltar"*. Cada secao e um deles, no molde das nove primeiras
/// (`GameServer.ValidacoesTeste.cs`): a recusa com a frase, nenhum efeito colateral, o caminho legitimo de pe, e o
/// defeito injetado do conserto (`GameServer.Validacoes.cs`, `GameServer.Atividade.cs`) reprovando a prova.
///
///   10. `C2S.Activity` -- treinar e meditar eram so DECLARADOS (`Train.dm:72`, `Meditate.dm:23`).
///   11. Os teleportes que furavam prisao (ponto cronico, salto de planeta, Transmissao, carona, carimbo, duelo).
///   12. `PodeAtirar` nao perguntava o atordoamento (`customattacks.dm:486`).
///   13. O Zanzoken e o avanco das tecnicas conferiam parede so em mapa de arquivo.
///   14. O Bio-Android Lab atendia qualquer um (`DNALabs.dm:371`, `:414`).
///   15. `forma <id>` passava por cima da posse.
///   16. A skill emprestada pela fusao era reembolsada.
///   17. `sala_soltar`: o Guardiao solta do Templo.
///
/// A SECAO 10 USA O HOST: a conferencia do treino so vale pra corpo que um CLIENTE dirige (`Peer` nao nulo), e
/// corpo forjado nao tem cliente. As outras usam corpos forjados.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>(palco) Poe um corpo forjado noutra zona com as listas de zona em dia -- o `MoveToZone` sem os pacotes.</summary>
	private void ValMover(ServerPlayer p, ZoneKey zona, Vec2 pos)
	{
		ZoneList(p.Zone.Hash).Remove(p);
		p.Zone = zona;
		p.Pos = pos;
		ZoneList(zona.Hash).Add(p);
	}

	// =====================================================================
	// 10) treinar e meditar
	// =====================================================================
	private void ValAtividade(ServerPlayer host)
	{
		GD.Print("[validacoes] -- 10) `C2S.Activity`: treinar e meditar sao PEDIDOS, e quem confere e o servidor (`Train.dm:72`, `Meditate.dm:23`) --");
		const int T = ZoneCollision.TileSize;
		Fighter f = host.Ficha;
		Jandirus.Core.Combat.CombatState c = host.Combate;

		// ---- a foto do host: a secao mexe no corpo de quem roda a bancada, e ele sai como entrou
		(double Bp, double Escondido, double Buffer, double BaseKi) ganhos = (f.BP, f.hiddenpotential, f.BPBuffer, f.baseKi);
		(ZoneKey Zona, Vec2 Pos, bool Voando, float Altitude, bool Nadando, bool Moving) corpo =
			(host.Zone, host.Pos, host.Voando, host.Altitude, host.Nadando, host.Moving);
		long relogio = AdiantoDoRelogioDeTeste;
		List<(int Quem, string Id, long Ms)>? escutaDeAntes = EscutaDeEfeitos;
		List<(int Quem, string Id, long Ms)> efeitos = [];
		EscutaDeEfeitos = efeitos;

		void DePe()
		{
			host.Moving = false;
			host.Voando = false;
			host.Altitude = 0f;
			host.Nadando = false;
			host.AgarradoPorId = 0;
			f.KO = f.dead = false;
			c.NocauteRestante = 0;
			c.EmCombate = c.LutandoDeVerdade = c.BrigandoComAlguem = 0;
			host.Zone = corpo.Zona;
			host.Pos = corpo.Pos;
			DefinirAtividade(host, Protocol.Activity.Parado);
			efeitos.Clear();
		}
		// O QUE O CLIENTE OUVE: o efeito que faz a pose e o HUD dele largarem a atividade.
		bool AvisouOCliente() => efeitos.Exists(e => e.Quem == host.Id && e.Id == TreinoEMeditacao.EfeitoDeQueCaiu && e.Ms == 0);
		// O GANHO, PELO FUNIL DE PRODUCAO: N voltas do `Treinar` (o que o `TickFichas` chama a 5 Hz).
		double Total() => f.BP + f.hiddenpotential + f.baseKi;
		double Rende(int voltas = 6)
		{
			double antes = Total();
			for (int i = 0; i < voltas; i++) Treinar(host);
			return Total() - antes;
		}
		void PassarDaCarencia() => AdiantoDoRelogioDeTeste += CarenciaDeChegadaMs + 100;
		string Estado() => $"train {f.train}, med {f.med}, avisos ao cliente {efeitos.Count}";

		try
		{
			// ---- o caminho legitimo, primeiro: sem ele as recusas de baixo nao provam nada
			DePe();
			List<string> comeca = Ouvir(() => DefinirAtividade(host, Protocol.Activity.Treinando));
			double rendeu = Rende();
			ValCheca("(montagem) parado, de pe e em paz o host TREINA: o pedido passa, o ganho corre e nada e dito ao cliente",
					f.train && rendeu > 0 && !AvisouOCliente(), $"ganho {rendeu:0.######}, {Estado()} | {Ultimos(comeca)}");
			DePe();
			DefinirAtividade(host, Protocol.Activity.Meditando);
			double meditou = Rende();
			ValCheca("(montagem) ...e MEDITA", f.med && meditou > 0 && !AvisouOCliente(), $"ganho {meditou:0.######}, {Estado()}");

			// =====================================================================
			// O PEDIDO RECUSADO
			// =====================================================================
			(string Nome, Protocol.Activity Qual, Action Por, string Trecho)[] recusas =
			[
				("VOANDO (`!flight`, `Train.dm:72`)", Protocol.Activity.Treinando, () => host.Voando = true, "pouse antes"),
				("NOCAUTEADO (`!KO`, `Train.dm:72`)", Protocol.Activity.Treinando, () => f.KO = true, "caído"),
				("NADANDO (`!swim`, `Meditate.dm:23`)", Protocol.Activity.Meditando, () => host.Nadando = true, "dentro d'água"),
				("NO MEIO DE UMA BRIGA", Protocol.Activity.Meditando, () => c.EntrarEmCombate(), "no meio de uma briga"),
				("PRESO NUM AGARRAO", Protocol.Activity.Meditando, () => host.AgarradoPorId = 987_654, "agarrão"),
			];
			foreach ((string nome, Protocol.Activity qual, Action por, string trecho) in recusas)
			{
				DePe();
				por();
				List<string> falas = Ouvir(() => DefinirAtividade(host, qual));
				double ganho = Rende();
				ValCheca($"**{nome}, O PEDIDO DE {(qual == Protocol.Activity.Meditando ? "MEDITAR" : "TREINAR")} E RECUSADO**: a atividade nao liga, o dono le por que, o cliente "
						+ "e avisado (larga a pose) e ganho nenhum corre",
						!f.train && !f.med && Disse(falas, trecho) && AvisouOCliente() && ganho == 0,
						$"ganho {ganho:0.######}, {Estado()} | {Ultimos(falas)}");

				AtividadeSemConferenciaDeTeste = true;
				try
				{
					DePe();
					por();
					DefinirAtividade(host, qual);
					double comDefeito = Rende();
					ValCheca("(defeito injetado: o servidor so anota o que o cliente declara) a prova de cima REPROVA -- a atividade liga e o "
							+ "ganho corre",
							(f.train || f.med) && comDefeito > 0 && !AvisouOCliente(), $"ganho {comDefeito:0.######}, {Estado()}");
				}
				finally { AtividadeSemConferenciaDeTeste = false; }
			}

			// O QUE O ORIGINAL DEIXA, E CONTINUA VALENDO
			DePe();
			host.Nadando = true;
			DefinirAtividade(host, Protocol.Activity.Treinando);
			bool treinaNadando = f.train;
			DePe();
			f.dead = true;
			host.Zone = ZoneKey.Premade(Alem.ZonaDoOutroMundo);   // (palco) so o campo: o morto de pe
			DefinirAtividade(host, Protocol.Activity.Treinando);
			bool treinaMorto = f.train && host.MortoDePe;
			DePe();
			f.dead = true;                                         // ...e o CADAVER, no chao dos vivos
			List<string> cadaver = Ouvir(() => DefinirAtividade(host, Protocol.Activity.Treinando));
			ValCheca("O QUE O ORIGINAL DEIXA CONTINUA VALENDO: treinar nadando (o `Train()` nao pergunta `swim`) e o MORTO DE PE treinar no "
					+ "Outro Mundo (nem `dead`) -- quem nao treina e o cadaver, antes da viagem",
					treinaNadando && treinaMorto && !f.train && Disse(cadaver, "caído"),
					$"nadando {treinaNadando}, morto de pe {treinaMorto}, cadaver {f.train} | {Ultimos(cadaver)}");

			// =====================================================================
			// A ATIVIDADE QUE CAI
			// =====================================================================
			(string Nome, Protocol.Activity Qual, Action Acontece, string Trecho)[] quedas =
			[
				("DECOLA (`start_flying()` chama `Deoccupy()`, `flying.dm:107-108`)", Protocol.Activity.Treinando, () => host.Voando = true, "no ar"),
				("ANDA (`if(curdir) src.Deoccupy()`, `movement handler.dm:217-219`)", Protocol.Activity.Treinando, () => host.Moving = true, "para de treinar"),
				("SAI DO LUGAR (dois tiles, sem dizer que andou)", Protocol.Activity.Meditando,
					() => host.Pos = corpo.Pos + new Vec2(2 * T, 0), "para de meditar"),
				("ENTRA NUMA BRIGA", Protocol.Activity.Meditando, () => c.EntrarEmCombate(), "a briga interrompe"),
				("CAI NUM AGARRAO", Protocol.Activity.Meditando, () => host.AgarradoPorId = 987_654, "agarrão"),
			];
			foreach ((string nome, Protocol.Activity qual, Action acontece, string trecho) in quedas)
			{
				DePe();
				DefinirAtividade(host, qual);
				PassarDaCarencia();
				double antes = Rende(3);
				efeitos.Clear();
				acontece();
				double depois = 0;
				List<string> falas = Ouvir(() => depois = Rende());
				ValCheca($"**QUEM {(qual == Protocol.Activity.Meditando ? "MEDITA" : "TREINA")} E {nome}, PARA**: ate ali rendia; dali em diante a atividade cai no "
						+ "servidor, o dono le por que, o cliente e avisado e o ganho para",
						antes > 0 && !f.train && !f.med && Disse(falas, trecho) && AvisouOCliente() && depois == 0,
						$"antes {antes:0.######}, depois {depois:0.######}, {Estado()} | {Ultimos(falas)}");

				AtividadeSemConferenciaDeTeste = true;
				try
				{
					DePe();
					DefinirAtividade(host, qual);
					PassarDaCarencia();
					acontece();
					double comDefeito = Rende();
					ValCheca("(defeito injetado: o servidor so anota o que o cliente declara) a prova de cima REPROVA -- a atividade segue "
							+ "ligada e o ganho continua correndo",
							(f.train || f.med) && comDefeito > 0 && !AvisouOCliente(), $"ganho {comDefeito:0.######}, {Estado()}");
				}
				finally { AtividadeSemConferenciaDeTeste = false; }
			}

			// ---- o nocaute: o Core ja desligava; o que faltava era o cliente saber
			DePe();
			DefinirAtividade(host, Protocol.Activity.Meditando);
			PassarDaCarencia();
			Rende(1);
			efeitos.Clear();
			c.Nocautear(5);
			bool oCoreDesligou = !f.med;
			Treinar(host);
			ValCheca("**O NOCAUTE DERRUBA NAS DUAS PONTAS**: o `Nocautear` desliga a meditacao (`KO.dm:56-57`), e a volta de ganho seguinte "
					+ "CONTA AO CLIENTE -- ele ficava em pose de meditar, com o HUD dizendo que meditava, ate o dono apertar M de novo",
					oCoreDesligou && AvisouOCliente(), Estado());

			// ---- parar pela propria tecla nao volta como aviso
			DePe();
			DefinirAtividade(host, Protocol.Activity.Treinando);
			efeitos.Clear();
			DefinirAtividade(host, Protocol.Activity.Parado);
			Treinar(host);
			ValCheca("...e quem para pela PROPRIA tecla nao recebe aviso nenhum de volta (o cliente ja sabe)", !f.train && !AvisouOCliente(), Estado());

			// =====================================================================
			// A CARENCIA DE CHEGADA
			// =====================================================================
			DePe();
			host.Moving = true;                                    // o "parei" do cliente ainda esta no fio
			DefinirAtividade(host, Protocol.Activity.Treinando);
			double naCarencia = Rende(2);
			bool esperou = f.train && naCarencia == 0 && !AvisouOCliente();
			host.Moving = false;                                   // ...chegou
			double parado = Rende();
			ValCheca("A CARENCIA DE CHEGADA: o pedido (canal confiavel) pode ser lido antes do ultimo passo (o outro canal) -- por meio "
					+ "segundo o servidor NAO PAGA e nao derruba, e quando o corpo para o treino rende",
					esperou && f.train && parado > 0 && !AvisouOCliente(),
					$"na carencia {naCarencia:0.######}, parado {parado:0.######}, {Estado()}");

			DePe();
			host.Moving = true;
			DefinirAtividade(host, Protocol.Activity.Treinando);
			double andando = Rende(2);
			PassarDaCarencia();
			List<string> caiu = Ouvir(() => andando += Rende(2));
			ValCheca("...e quem pede andando e CONTINUA andando cai na primeira volta depois dela, sem ter ganhado nada",
					!f.train && andando == 0 && Disse(caiu, "para de treinar") && AvisouOCliente(),
					$"ganho {andando:0.######}, {Estado()} | {Ultimos(caiu)}");

			// =====================================================================
			// O QUE O SERVIDOR MESMO MOVE, E O QUE ELE MESMO LIGA
			// =====================================================================
			DePe();
			DefinirAtividade(host, Protocol.Activity.Meditando);
			PassarDaCarencia();
			Rende(1);
			efeitos.Clear();
			host.Zone = DimensaoMental.De(host.Id);                // (palco) so os campos: o que o mergulho na mente faz
			host.Pos = corpo.Pos + new Vec2(40 * T, 0);
			List<string> naMente = Ouvir(() => Rende(2));
			ValCheca("LEVADO PRA OUTRA ZONA PELO SERVIDOR (o mergulho na mente comeca da meditacao; a porta da Sala do Tempo), o corpo NAO "
					+ "cai: a ancora se refaz no lugar novo, calada",
					f.med && !AvisouOCliente() && naMente.Count == 0, $"{Estado()} | {Ultimos(naMente)}");

			DePe();
			f.med = true;                                          // o que o `Ki_Targets` escreve (`med = 1`, `Meditate.dm:248`)
			_alvosDeKiG12[host.Id] = new EstadoDosAlvosDeKiG12
			{
				Zona = host.Zone.Hash, ProximoAlvoEm = double.MaxValue, ProximoPassoEm = double.MaxValue,
			};
			double sentado = Rende();
			host.Moving = true;
			double andandoComAlvos = Rende();
			bool ficou = f.med && !AvisouOCliente();
			host.Moving = false;
			double sentadoDeNovo = Rende();
			_alvosDeKiG12.Remove(host.Id);
			ValCheca("OS ALVOS DE KI (`Ki_Targets`) SAO A EXCECAO QUE ANDA: sem o clique do DM (`Meditate.dm:280-289`) so se chega nas "
					+ "esferas andando -- o `med` fica (e ele que mantem os alvos) e o que NAO corre e o ganho de meditacao enquanto o corpo anda",
					sentado > 0 && ficou && andandoComAlvos == 0 && sentadoDeNovo > 0,
					$"sentado {sentado:0.######}, andando {andandoComAlvos:0.######}, sentado de novo {sentadoDeNovo:0.######}, ficou {ficou}");

			DePe();
			ServerPlayer habitante = ValGente("HabitanteDaBancada", "Human", CorredorLivre(24));
			habitante.Ficha.train = true;
			habitante.Moving = true;
			habitante.Voando = true;
			Treinar(habitante);
			ValCheca("O CORPO QUE O SERVIDOR DIRIGE NAO E CONFERIDO (o habitante que treina pela rotina, o corpo largado de quem mergulhou): "
					+ "quem escreve o `train` e o `Moving` dele e o proprio servidor", habitante.Ficha.train);
			habitante.Ficha.train = habitante.Moving = habitante.Voando = false;
		}
		finally
		{
			AtividadeSemConferenciaDeTeste = false;
			_alvosDeKiG12.Remove(host.Id);
			AdiantoDoRelogioDeTeste = relogio;
			DePe();
			host.Voando = corpo.Voando;
			host.Altitude = corpo.Altitude;
			host.Nadando = corpo.Nadando;
			host.Moving = corpo.Moving;
			(f.BP, f.hiddenpotential, f.BPBuffer, f.baseKi) = ganhos;
			EscutaDeEfeitos = escutaDeAntes;
		}
	}

	// =====================================================================
	// 11) os teleportes e a prisao
	// =====================================================================
	private void ValTeleportes(ZoneKey terra)
	{
		GD.Print("[validacoes] -- 11) os teleportes nao furam prisao: UMA pergunta (`PorQueNaoSaiDaqui`), pra quem salta e pra quem vai de carona --");
		const int T = ZoneCollision.TileSize;
		ZoneKey sala = ZoneKey.Premade(ZonaDaSala);
		Vec2 dentro = new((EntradaByondX - 1) * T + T / 2f, 345 * T + T / 2f);
		Vec2 palco = CorredorLivre(24);

		void Prender(ServerPlayer p, Vec2? onde = null) { ValMover(p, sala, onde ?? dentro); p.SalaPreso = true; }
		void SoltarNaTerra(ServerPlayer p, Vec2 onde) { p.SalaPreso = false; ValMover(p, terra, onde); }

		// ---- a pergunta, lugar por lugar
		ServerPlayer preso = ValGente("PresoDaBancada", "Human", palco, kiMin: 100_000);
		string livre = PorQueNaoSaiDaqui(preso);
		Prender(preso);
		string daSala = PorQueNaoSaiDaqui(preso);
		preso.SalaPreso = false;
		string emSessao = PorQueNaoSaiDaqui(preso);          // dentro da Sala, mas com a sessao valendo: sai quando quiser
		ValMover(preso, InteriorDoMajin.De(777_002), InteriorDoMajin.PixelDe((50, 50)));
		string doMajin = PorQueNaoSaiDaqui(preso);
		ValMover(preso, ZonaDoSelo(777_003), PosDoSelo);
		string daFusao = PorQueNaoSaiDaqui(preso);
		ValMover(preso, DimensaoMental.De(777_004), PosDoSelo);
		string daMente = PorQueNaoSaiDaqui(preso);
		ValCheca("**A PERGUNTA DE TODO TELEPORTE**: solto na Terra (e em sessao na Sala) o corpo sai; PRESO na Sala do Tempo, dentro de um "
				+ "Majin, no bolso de uma fusao e numa mente, nao -- e cada um ouve o seu porque",
				livre.Length == 0 && emSessao.Length == 0 && daSala.Contains("preso na Sala do Tempo") && doMajin.Contains("vencer a imagem")
				&& daFusao.Contains("passageiro de uma fusão") && daMente.Contains("saia do transe"),
				$"livre '{livre}' | sessao '{emSessao}' | sala '{daSala}' | majin '{doMajin}' | fusao '{daFusao}' | mente '{daMente}'");
		SoltarNaTerra(preso, palco);

		// =====================================================================
		// O PONTO CRONICO (`Chrono_Trigger`, `kanassa-jin.dm:108`)
		// =====================================================================
		Ouvir(() => GatilhoCronicoG4(preso));
		bool cravou = _pontoCronoG4.ContainsKey(preso.Id);
		Prender(preso);
		List<string> doPreso = Ouvir(() => GatilhoCronicoG4(preso));
		ValCheca("**O PRESO DA SALA DO TEMPO NAO VOLTA NO TEMPO PRA FORA DELA**: com o ponto cravado na Terra antes de entrar, o verb e "
				+ "recusado -- ele continua la dentro, preso, e o ponto continua cravado",
				cravou && NaSala(preso) && preso.SalaPreso && _pontoCronoG4.ContainsKey(preso.Id) && Disse(doPreso, "preso na Sala do Tempo"),
				$"cravou {cravou}, zona {preso.Zone.Name}, ponto {_pontoCronoG4.ContainsKey(preso.Id)} | {Ultimos(doPreso)}");

		TeleporteFuraPrisaoDeTeste = true;
		try
		{
			Ouvir(() => GatilhoCronicoG4(preso));
			ValCheca("(defeito injetado: o teleporte nao pergunta de onde nao se sai) a prova de cima REPROVA -- o preso reaparece na "
					+ "Terra, no ponto que cravou antes de entrar",
					preso.Zone.Hash == terra.Hash && !_pontoCronoG4.ContainsKey(preso.Id), preso.Zone.Name);
		}
		finally { TeleporteFuraPrisaoDeTeste = false; _pontoCronoG4.Remove(preso.Id); }

		preso.SalaPreso = false;
		ValMover(preso, sala, dentro);
		List<string> ancora = Ouvir(() => GatilhoCronicoG4(preso));
		bool naoCravou = !_pontoCronoG4.ContainsKey(preso.Id);
		TeleporteFuraPrisaoDeTeste = true;
		try { GatilhoCronicoG4(preso); }
		finally { TeleporteFuraPrisaoDeTeste = false; }
		bool comDefeitoCrava = _pontoCronoG4.Remove(preso.Id);
		ValCheca("...e DENTRO da Sala nao se crava ponto (seria a chave permanente dela: sem Guardiao, sem lotacao, sem recarga) -- "
				+ "com o defeito injetado ele se crava",
				naoCravou && Disse(ancora, "âncora") && comDefeitoCrava, $"sem ponto {naoCravou}, com o defeito {comDefeitoCrava} | {Ultimos(ancora)}");

		SoltarNaTerra(preso, palco);
		Ouvir(() => GatilhoCronicoG4(preso));
		preso.Pos = palco + new Vec2(6 * T, 0);
		List<string> volta = Ouvir(() => GatilhoCronicoG4(preso));
		ValCheca("O CAMINHO LEGITIMO: solto, o ponto se crava e devolve o corpo ao lugar",
				Vec2.Distance(preso.Pos, palco) < 2 && !_pontoCronoG4.ContainsKey(preso.Id) && Disse(volta, "o tempo se dobra"),
				$"{preso.Pos} | {Ultimos(volta)}");

		// =====================================================================
		// O SALTO DE PLANETA (`PodeSaltarDePlaneta`: RiftTeleport, Kai Kai, Devil Bringer, Atalho Sagrado)
		// =====================================================================
		Prender(preso);
		preso.Ficha.Ki = preso.Ficha.MaxKi;
		double ki = preso.Ficha.Ki;
		List<string> rasgo = Ouvir(() => RasgoDimensionalG4(preso, "Earth"));
		bool ficouNaSala = NaSala(preso) && Math.Abs(preso.Ficha.Ki - ki) < 1e-9 && Disse(rasgo, "preso na Sala do Tempo");
		preso.SalaPreso = false;
		ValMover(preso, ZonaDoSelo(777_003), PosDoSelo);
		List<string> doBolso = Ouvir(() => RasgoDimensionalG4(preso, "Earth"));
		ValCheca("**O SALTO DE PLANETA** (a porta do RiftTeleport, do Kai Kai, do Devil Bringer e do Atalho Sagrado): o preso da Sala "
				+ "nao rasga o ar pra fora -- fica, com o Ki inteiro --, e o passageiro de uma fusao nao sai do bolso dele",
				ficouNaSala && EhOSelo(preso.Zone) && Disse(doBolso, "passageiro de uma fusão"),
				$"ficou {ficouNaSala}, zona {preso.Zone.Name} | {Ultimos(rasgo)} || {Ultimos(doBolso)}");

		Prender(preso);
		_saltoPronto.Remove(preso.Id);
		TeleporteFuraPrisaoDeTeste = true;
		try
		{
			Ouvir(() => RasgoDimensionalG4(preso, "Earth"));
			ValCheca("(defeito injetado: o teleporte nao pergunta de onde nao se sai) a prova de cima REPROVA -- o preso rasga o ar e "
					+ "chega na Terra", preso.Zone.Hash == terra.Hash, preso.Zone.Name);
		}
		finally { TeleporteFuraPrisaoDeTeste = false; _saltoPronto.Remove(preso.Id); }
		SoltarNaTerra(preso, palco);
		preso.Ficha.Ki = preso.Ficha.MaxKi;

		// =====================================================================
		// A TRANSMISSAO (`Instant_Transmission`, `yardrat.dm:99`): o pedido e a chegada
		// =====================================================================
		ServerPlayer amigo = ValGente("AmigoDaBancada", "Human", palco + new Vec2(4 * T, 0));
		ServerPlayer yardrat = ValGente("YardratDaBancada", "Yardrat", palco + new Vec2(0, 4 * T), kiMin: 100_000);
		Prender(preso);
		List<string> pedido = Ouvir(() => TeletransporteG11(preso, ""));
		double kiDoPreso = preso.Ficha.Ki;
		List<string> chegada = Ouvir(() => ChegarPelaTransmissaoG11(preso, new TransmissaoG11 { Alvo = amigo.Id, Ancora = preso.Pos, Kireq = 10 }));
		ValCheca("**A TRANSMISSAO**: o preso nem comeca a se concentrar, e quem ficou preso DURANTE a concentracao nao chega -- "
				+ "continua na Sala, sem pagar",
				Disse(pedido, "preso na Sala do Tempo") && !_transmissaoG11.ContainsKey(preso.Id) && NaSala(preso)
				&& Math.Abs(preso.Ficha.Ki - kiDoPreso) < 1e-9 && Disse(chegada, "preso na Sala do Tempo"),
				$"zona {preso.Zone.Name} | {Ultimos(pedido)} || {Ultimos(chegada)}");

		double kiDoYardrat = yardrat.Ficha.Ki;
		List<string> fechada = Ouvir(() => ChegarPelaTransmissaoG11(yardrat, new TransmissaoG11 { Alvo = preso.Id, Ancora = yardrat.Pos, Kireq = 10 }));
		ValCheca("...e NAO SE CHEGA na Sala do Tempo seguindo a assinatura de quem entrou nela depois do pedido (a lista so tira quem ja "
				+ "estava la na hora de listar): quem vinha fica onde esta, sem pagar",
				yardrat.Zone.Hash == terra.Hash && Math.Abs(yardrat.Ficha.Ki - kiDoYardrat) < 1e-9 && Disse(fechada, "nao se chega assim"),
				$"zona {yardrat.Zone.Name} | {Ultimos(fechada)}");

		TeleporteFuraPrisaoDeTeste = true;
		try
		{
			Ouvir(() => ChegarPelaTransmissaoG11(yardrat, new TransmissaoG11 { Alvo = preso.Id, Ancora = yardrat.Pos, Kireq = 10 }));
			bool entrou = NaSala(yardrat);
			Ouvir(() => ChegarPelaTransmissaoG11(preso, new TransmissaoG11 { Alvo = amigo.Id, Ancora = preso.Pos, Kireq = 10 }));
			ValCheca("(defeito injetado: o teleporte nao pergunta) as duas provas de cima REPROVAM -- um entra na Sala sem porta, sem chave "
					+ "e sem recarga, e o preso sai dela",
					entrou && preso.Zone.Hash == terra.Hash, $"yardrat entrou {entrou}, preso em {preso.Zone.Name}");
		}
		finally { TeleporteFuraPrisaoDeTeste = false; }
		SoltarNaTerra(preso, palco);
		SoltarNaTerra(yardrat, palco + new Vec2(0, 4 * T));

		// =====================================================================
		// A CARONA (`oview(1)` do Kai Kai, da Transmissao e do Atalho Sagrado)
		// =====================================================================
		Prender(preso);
		ValMover(yardrat, sala, dentro + new Vec2(T, 0));     // em sessao, solto: o que salta
		ValMover(amigo, sala, dentro + new Vec2(T, T));       // em sessao, solto: a carona legitima
		List<ServerPlayer> caronas = LevarCaronas(yardrat);
		TeleporteFuraPrisaoDeTeste = true;
		List<ServerPlayer> comDefeito;
		try { comDefeito = LevarCaronas(yardrat); }
		finally { TeleporteFuraPrisaoDeTeste = false; }
		ValCheca("**QUEM NAO PODE SAIR NAO VAI DE CARONA**: ao lado de quem salta, o PRESO fica e o solto vai -- com o defeito injetado "
				+ "o preso sai da Sala encostado em quem saltou",
				!caronas.Contains(preso) && caronas.Contains(amigo) && comDefeito.Contains(preso),
				$"caronas [{string.Join(", ", caronas.Select(o => o.Name))}], com o defeito [{string.Join(", ", comDefeito.Select(o => o.Name))}]");
		SoltarNaTerra(yardrat, palco + new Vec2(0, 4 * T));
		SoltarNaTerra(amigo, palco + new Vec2(4 * T, 0));

		// =====================================================================
		// O CARIMBO DO JUIZ (`Go_To_Heaven_Or_Hell`, `OtherworldRankSkills.dm:188`)
		// =====================================================================
		List<string> carimbo = Ouvir(() => JulgarG8(preso, "ceu"));
		bool juizFicou = NaSala(preso) && Disse(carimbo, "preso na Sala do Tempo");
		TeleporteFuraPrisaoDeTeste = true;
		try { Ouvir(() => JulgarG8(preso, "ceu")); }
		finally { TeleporteFuraPrisaoDeTeste = false; }
		ValCheca("**O CARIMBO DO JUIZ** so perguntava pelo nocaute: preso na Sala, o juiz nao atravessa pro Ceu -- com o defeito injetado, atravessa",
				juizFicou && preso.Zone.Name == Alem.ZonaDoCeu, $"ficou {juizFicou}, com o defeito em {preso.Zone.Name} | {Ultimos(carimbo)}");
		SoltarNaTerra(preso, palco);

		ValSecao("11: o duelo pelo titulo", () => ValODueloEAPrisao(terra, sala, dentro, palco));
	}

	/// <summary>11, o fim) O DUELO PELO TITULO: o aceite leva o desafiante ate o Deus, e o fim devolve os dois ao berco.</summary>
	private void ValODueloEAPrisao(ZoneKey terra, ZoneKey sala, Vec2 dentro, Vec2 palco)
	{
		const int T = ZoneCollision.TileSize;
		(long Desde, long Ultimo, int Adiamentos, long Semana, bool EmDuelo) titulo =
			(_duelo.TituloDesde, _duelo.UltimoDuelo, _duelo.Adiamentos, _duelo.SemanaDosAdiamentos, _duelo.EmDuelo);
		(string Esperando, long Prazo, string Dono, string Desafiante, long Comeca, long Acaba) pendencia =
			(_desafianteEsperando, _prazoDaResposta, _dueloDono, _dueloDesafiante, _dueloComeca, _dueloAcaba);

		Vec2 doDeus = palco + new Vec2(10 * T, 0), doDesafiante = palco + new Vec2(16 * T, 0);
		ServerPlayer deus = ValGente("DeusDaBancada", "Saiyan", doDeus, bp: 50_000_000);
		ServerPlayer desafiante = ValGente("DesafianteDaBancada", "Saiyan", doDesafiante, bp: 50_000_000);
		desafiante.Ficha.godki = new GodKiState { awakened = true, mastery = 70 };

		void AbrirOTrono()
		{
			_tronos[CargoEmDisputa] = deus.Conta;
			long agora = NowMs();
			_duelo.TituloDesde = agora - Duelo.Carencia - 1;
			_duelo.UltimoDuelo = agora - Duelo.Intervalo - 1;
			_duelo.EmDuelo = false;
			_desafianteEsperando = _dueloDono = _dueloDesafiante = "";
		}

		try
		{
			AbrirOTrono();

			// ---- o preso desafia
			ValMover(desafiante, sala, dentro);
			desafiante.SalaPreso = true;
			List<string> daPrisao = Ouvir(() => VerboDesafiar(desafiante));
			ValCheca("**O DESAFIO PELO TITULO NAO E A CHAVE DA PRISAO** (o aceite leva o desafiante ate o Deus, e o fim devolve os dois ao "
					+ "berco): o preso da Sala nao desafia -- nada fica pendente e o Deus nao ouve nada",
					_desafianteEsperando.Length == 0 && !_duelo.EmDuelo && Disse(daPrisao, "preso na Sala do Tempo") && !Disse(daPrisao, "te desafia"),
					$"pendente '{_desafianteEsperando}' | {Ultimos(daPrisao)}");

			// ---- desafia solto, e fica preso antes do aceite
			desafiante.SalaPreso = false;
			ValMover(desafiante, terra, doDesafiante);
			Ouvir(() => VerboDesafiar(desafiante));
			bool pendente = string.Equals(_desafianteEsperando, desafiante.Conta, StringComparison.OrdinalIgnoreCase);
			int adiamentos = _duelo.Adiamentos;
			ValMover(desafiante, sala, dentro);
			desafiante.SalaPreso = true;
			List<string> aceite = Ouvir(() => VerboResponderDesafio(deus, aceitou: true));
			ValCheca("...e O ACEITE RECONFERE (entre o desafio e a resposta passa ate um minuto e meio): o desafiante que ficou preso nao e "
					+ "buscado -- o desafio se desfaz sem cobrar adiamento, e ninguem e movido",
					pendente && NaSala(desafiante) && _dueloDono.Length == 0 && _desafianteEsperando.Length == 0 && !_duelo.EmDuelo
					&& _duelo.Adiamentos == adiamentos && Disse(aceite, "se desfaz"),
					$"pendente {pendente}, zona {desafiante.Zone.Name}, duelo '{_dueloDono}', adiamentos {adiamentos}->{_duelo.Adiamentos} | {Ultimos(aceite)}");

			// ---- o Deus que nao pode sair nao aceita (e o desafio continua de pe)
			desafiante.SalaPreso = false;
			ValMover(desafiante, terra, doDesafiante);
			AbrirOTrono();
			Ouvir(() => VerboDesafiar(desafiante));
			ValMover(deus, DimensaoMental.De(deus.Id), PosDoSelo);
			List<string> emTranse = Ouvir(() => VerboResponderDesafio(deus, aceitou: true));
			ValCheca("...e o DEUS que nao pode sair de onde esta (em transe) nao aceita: o desafio continua de pe, ele acorda e aceita, ou "
					+ "adia -- e o desafiante nao e levado pra dentro da mente dele",
					_dueloDono.Length == 0 && string.Equals(_desafianteEsperando, desafiante.Conta, StringComparison.OrdinalIgnoreCase)
					&& desafiante.Zone.Hash == terra.Hash && Disse(emTranse, "saia do transe"),
					$"duelo '{_dueloDono}', pendente '{_desafianteEsperando}', desafiante em {desafiante.Zone.Name} | {Ultimos(emTranse)}");
			ValMover(deus, terra, doDeus);

			// ---- o defeito: o aceite busca o preso
			ValMover(desafiante, sala, dentro);
			desafiante.SalaPreso = true;
			TeleporteFuraPrisaoDeTeste = true;
			try
			{
				Ouvir(() => VerboResponderDesafio(deus, aceitou: true));
				ValCheca("(defeito injetado: o teleporte nao pergunta de onde nao se sai) as provas de cima REPROVAM -- o aceite busca o "
						+ "preso dentro da Sala e o poe ao lado do Deus",
						desafiante.Zone.Hash == deus.Zone.Hash && _dueloDono.Length > 0, $"desafiante em {desafiante.Zone.Name}, duelo '{_dueloDono}'");
			}
			finally { TeleporteFuraPrisaoDeTeste = false; }

			// ---- o caminho legitimo
			desafiante.SalaPreso = false;
			if (desafiante.Zone.Hash != terra.Hash) ValMover(desafiante, terra, doDesafiante);
			desafiante.Pos = doDesafiante;
			AbrirOTrono();
			Ouvir(() => VerboDesafiar(desafiante));
			Ouvir(() => VerboResponderDesafio(deus, aceitou: true));
			ValCheca("O CAMINHO LEGITIMO: os dois soltos, o aceite poe o desafiante a quatro tiles do Deus e o duelo comeca",
					_dueloDono.Length > 0 && desafiante.Zone.Hash == deus.Zone.Hash
					&& Math.Abs(desafiante.Pos.X - deus.Pos.X) <= 4 * T + 1, $"duelo '{_dueloDono}', {desafiante.Pos} x {deus.Pos}");
		}
		finally
		{
			TeleporteFuraPrisaoDeTeste = false;
			desafiante.SalaPreso = false;
			(_duelo.TituloDesde, _duelo.UltimoDuelo, _duelo.Adiamentos, _duelo.SemanaDosAdiamentos, _duelo.EmDuelo) = titulo;
			(_desafianteEsperando, _prazoDaResposta, _dueloDono, _dueloDesafiante, _dueloComeca, _dueloAcaba) = pendencia;
		}
	}

	// =====================================================================
	// 12) atirar atordoado
	// =====================================================================
	private void ValAtordoado()
	{
		GD.Print("[validacoes] -- 12) `PodeAtirar`: atordoado nao atira (o `!canfight` de todo verb de ki, `customattacks.dm:486`) --");
		Vec2 palco = CorredorLivre(24);
		ServerPlayer atirador = ValGente("AtiradorDaBancada", "Human", palco, bp: 50_000, kiMin: 5_000_000);
		atirador.Facing = Facing.East;

		atirador.Combate.Stun = 0.5;
		double ki = atirador.Ficha.Ki;
		int tiros = TirosDeG12(atirador).Count;
		List<string> falas = Ouvir(() => GuidedBall(atirador));
		bool aPortaAbre = PodeAtirar(atirador, 1, out string porque);
		ValCheca("**ATORDOADO NAO ATIRA** (`if(KO || med || train || !canfight || charging || beaming) return 0`, `customattacks.dm:486`): "
				+ "a porta de todo tiro recusa, a esfera nao nasce e o Ki nao e cobrado",
				!aPortaAbre && porque.Contains("atordoado") && Disse(falas, "atordoado") && TirosDeG12(atirador).Count == tiros
				&& Math.Abs(atirador.Ficha.Ki - ki) < 1e-9,
				$"porta {aPortaAbre} '{porque}', tiros {tiros}->{TirosDeG12(atirador).Count}, Ki {ki:0}->{atirador.Ficha.Ki:0} | {Ultimos(falas)}");

		AtiraAtordoadoDeTeste = true;
		try
		{
			Ouvir(() => GuidedBall(atirador));
			ValCheca("(defeito injetado: a porta nao pergunta o atordoamento) a prova de cima REPROVA -- a esfera nasce e o Ki e cobrado "
					+ "no meio do atordoamento",
					TirosDeG12(atirador).Count == tiros + 1 && atirador.Ficha.Ki < ki, $"tiros {TirosDeG12(atirador).Count}, Ki {atirador.Ficha.Ki:0}");
		}
		finally { AtiraAtordoadoDeTeste = false; }

		atirador.Combate.Stun = 0;
		atirador.Ficha.Ki = atirador.Ficha.MaxKi;
		int antes = TirosDeG12(atirador).Count;
		List<string> dePe = Ouvir(() => GuidedBall(atirador));
		ValCheca("O CAMINHO LEGITIMO: passado o atordoamento, a mesma esfera sai", TirosDeG12(atirador).Count == antes + 1, Ultimos(dePe));
		LimparProjeteisDeUmDono(atirador.Id, atirador.Zone.Hash);
	}

	// =====================================================================
	// 13) a parede do salto
	// =====================================================================
	private void ValParedeDoSalto()
	{
		GD.Print("[validacoes] -- 13) o Zanzoken e o avanco das tecnicas conferem parede na zona em que o corpo ESTA (e nao so em mapa de arquivo) --");
		const int T = ZoneCollision.TileSize;
		ZoneKey bolso = InteriorDoMajin.De(777_005);
		ZoneCollision planta = InteriorDoMajin.Planta().Colisao;
		Vec2 Pe(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);

		ValCheca("(montagem) uma zona sem mapa de arquivo (a planta do interior de um Majin): o catalogo nao a conhece, o funil de "
				+ "colisao sim, e a celula (0,50) e a parede dela",
				_catalogo?.Get(bolso)?.Mapa == null && ReferenceEquals(MapaDaZonaOuCatalogo(bolso), planta)
				&& planta.BlockedCell(0, 50) && !planta.BlockedCell(1, 50) && !planta.BlockedCell(2, 50) && !planta.BlockedCell(3, 50));

		// ---- a piscada
		ServerPlayer piscador = ValGente("PiscadorDaBancada", "Human", CorredorLivre(24), bp: 50_000, skills: [PathDoZanzoken], kiMin: 5_000_000);
		ValMover(piscador, bolso, Pe(1, 50));
		double ki = piscador.Ficha.Ki;
		Zanzoken(piscador, Pe(0, 50));
		ValCheca("**A PISCADA NAO ENTRA NA PAREDE** numa zona que nao e mapa de arquivo (planeta gerado, base, interior): o corpo fica "
				+ "onde estava e o Ki nao e cobrado",
				Vec2.Distance(piscador.Pos, Pe(1, 50)) < 1 && Math.Abs(piscador.Ficha.Ki - ki) < 1e-9, $"{piscador.Pos}, Ki {ki:0}->{piscador.Ficha.Ki:0}");

		ParedeSoDeCatalogoDeTeste = true;
		try
		{
			Zanzoken(piscador, Pe(0, 50));
			ValCheca("(defeito injetado: so o catalogo de mapas de arquivo e consultado) a prova de cima REPROVA -- o corpo pisca pra "
					+ "DENTRO da parede",
					planta.BlockedAt(piscador.Pos + new Vec2(0, MoveRules.FeetOffsetY)) && piscador.Ficha.Ki < ki, $"{piscador.Pos}");
		}
		finally { ParedeSoDeCatalogoDeTeste = false; }

		piscador.Pos = Pe(1, 50);
		piscador.Ficha.Ki = piscador.Ficha.MaxKi;
		Zanzoken(piscador, Pe(2, 50));
		ValCheca("O CAMINHO LEGITIMO: pro chao livre ao lado, a piscada sai", Vec2.Distance(piscador.Pos, Pe(2, 50)) < 1, $"{piscador.Pos}");

		// ---- o avanco das tecnicas de aproximacao (`AvancarG3`: Light Buster, jokenpo, os dashes de G7/G10)
		ServerPlayer investidor = ValGente("InvestidorDaBancada", "Human", CorredorLivre(24));
		ServerPlayer alemDaParede = ValGente("AlemDaParedeDaBancada", "Human", CorredorLivre(24));
		ValMover(investidor, bolso, Pe(3, 50));
		ValMover(alemDaParede, bolso, Pe(-2, 50));              // (palco) do lado de fora da parede do bolso
		AvancarG3(investidor, alemDaParede, 6 * T);
		bool parou = Vec2.Distance(investidor.Pos, Pe(3, 50)) < 1;
		ParedeSoDeCatalogoDeTeste = true;
		try { AvancarG3(investidor, alemDaParede, 6 * T); }
		finally { ParedeSoDeCatalogoDeTeste = false; }
		ValCheca("**O AVANCO DAS TECNICAS DE APROXIMACAO TAMBEM PARA NA PAREDE**: com o alvo do outro lado, o corpo nao sai do lugar -- "
				+ "com o defeito injetado ele atravessa",
				parou && investidor.Pos.X < Pe(1, 50).X, $"parou {parou}, com o defeito em {investidor.Pos}");
	}

	// =====================================================================
	// 14) o Bio-Android Lab
	// =====================================================================
	private void ValLabDeBio(ServerPlayer host)
	{
		GD.Print("[validacoes] -- 14) o Bio-Android Lab e de quem o ergueu (`DNALabs.dm:371-373`, `:414`) --");
		Vec2 palco = CorredorLivre(24);
		ServerPlayer dono = ValGente("CientistaDaBancada", "Human", palco);
		ServerPlayer intruso = ValGente("IntrusoDaBancada", "Human", palco + new Vec2(8, 0));
		ServerPlayer doador = ValGente("DoadorDaBancada", "Saiyan", palco + new Vec2(0, 8), bp: 900_000);
		var lab = new Obra
		{
			Id = 987_014, Tipo = "Android_Creation_Mainframe", Aparafusada = true, Lab = 2,
			X = palco.X, Y = palco.Y, DonoConta = dono.Conta, DonoNome = dono.Name,
		};
		lab.PorZona(dono.Zone);
		_noChao.Add(lab);
		try
		{
			// (palco) O DOADOR E GENTE: o `ColherDna` so aceita JOGADOR (`EhJogador` pede `Peer`), e corpo forjado nasce sem.
			doador.Peer = host.Peer;
			doador.Ficha.KO = true;
			ValCheca("(montagem) um Bio-Android Lab aparafusado, do cientista, com um jogador nocauteado ao alcance",
					LabDeBio(intruso) == lab && LabDeBio(dono) == lab && EhJogador(doador));

			// ---- colher
			List<string> agulha = Ouvir(() => ComandoDeTech(intruso, "colher_dna", ""));
			ValCheca("**O PAINEL ESTA TRANCADO NO DNA DO DONO** (`if(usr.signature != owner_sig)`, `DNALabs.dm:371-373`): um estranho nao "
					+ "colhe DNA no laboratorio alheio -- o tanque continua vazio e o caido nao leva agulha",
					lab.Fornada == null && Disse(agulha, "trancado no DNA de") && !Disse(agulha, "agulha"), Ultimos(agulha));

			LabSemDonoDeTeste = true;
			try
			{
				Ouvir(() => ComandoDeTech(intruso, "colher_dna", ""));
				ValCheca("(defeito injetado: o laboratorio atende qualquer um) a prova de cima REPROVA -- a primeira agulha do estranho faz "
						+ "DELE o criador da fornada, no laboratorio de outro",
						lab.Fornada is { Amostras.Count: 1 } g0 && g0.DonoConta == intruso.Conta, $"criador '{lab.Fornada?.DonoConta}'");
			}
			finally { LabSemDonoDeTeste = false; lab.Fornada = null; }

			List<string> doDono = Ouvir(() => ComandoDeTech(dono, "colher_dna", ""));
			ValCheca("O CAMINHO LEGITIMO: o dono colhe, e a amostra entra no tanque dele",
					lab.Fornada is { Amostras.Count: 1 } g1 && g1.DonoConta == dono.Conta, Ultimos(doDono));

			// ---- gestar
			List<string> fecha = Ouvir(() => ComandoDeTech(intruso, "gestar", ""));
			ValCheca("**...E NINGUEM FECHA O TANQUE DE OUTRO** (`Topic()`: `if(!usr || usr.signature != owner_sig) return`, `:414`): era "
					+ "o jeito de fazer a criatura matar o dono na hora e com o DNA que um estranho escolheu",
					lab.Fornada is { PrometidaEm: 0 } && Disse(fecha, "trancado no DNA de"), Ultimos(fecha));

			LabSemDonoDeTeste = true;
			try
			{
				Ouvir(() => ComandoDeTech(intruso, "gestar", ""));
				ValCheca("(defeito injetado: o laboratorio atende qualquer um) a prova de cima REPROVA -- o estranho fecha o tanque do dono",
						lab.Fornada is { PrometidaEm: > 0 });
			}
			finally { LabSemDonoDeTeste = false; if (lab.Fornada != null) { lab.Fornada.PrometidaEm = 0; lab.Fornada.DonoConta = dono.Conta; lab.Fornada.DonoAssinatura = ""; } }

			List<string> gesta = Ouvir(() => ComandoDeTech(dono, "gestar", ""));
			Gestacao? g = lab.Fornada;
			ValCheca("O CAMINHO LEGITIMO: o dono fecha o tanque, e a fornada guarda a ASSINATURA dele (`creator_sig = usr.signature`, `:441`)",
					g is { PrometidaEm: > 0 } && g.DonoConta == dono.Conta && g.DonoAssinatura.Length > 0 && g.DonoAssinatura == dono.Assinatura,
					$"assinatura '{g?.DonoAssinatura}' x '{dono.Assinatura}' | {Ultimos(gesta)}");
			if (g == null) return;

			// ---- quem a criatura mata: o PERSONAGEM, e nao outro da mesma conta
			ServerPlayer outroSlot = ValGente("OutroSlotDaBancada", "Human", palco + new Vec2(16, 0));
			outroSlot.Conta = dono.Conta;
			outroSlot.Slot = dono.Slot + 1;
			bool soODono = EhOCriadorDaFornada(g, dono) && !EhOCriadorDaFornada(g, outroSlot) && !EhOCriadorDaFornada(g, intruso);
			LabSemDonoDeTeste = true;
			bool comDefeito;
			try { comDefeito = EhOCriadorDaFornada(g, outroSlot); }
			finally { LabSemDonoDeTeste = false; }
			string assinatura = g.DonoAssinatura;
			g.DonoAssinatura = "";
			bool fornadaAntiga = EhOCriadorDaFornada(g, dono) && EhOCriadorDaFornada(g, outroSlot);
			g.DonoAssinatura = assinatura;
			ValCheca("**A CRIATURA MATA QUEM FECHOU O TANQUE** (`P.signature == creator_sig`, `:364`): outro personagem da mesma conta "
					+ "(outro slot) nao e o criador -- com o defeito injetado, e; e a fornada antiga, sem assinatura, continua valendo pela conta",
					soODono && comDefeito && fornadaAntiga, $"so o dono {soODono}, com o defeito {comDefeito}, fornada antiga {fornadaAntiga}");
		}
		finally
		{
			LabSemDonoDeTeste = false;
			doador.Peer = null;
			doador.Ficha.KO = false;
			lab.Fornada = null;
			_noChao.Remove(lab);
			GravarMundo();
		}
	}

	// =====================================================================
	// 15) forma <id> e a posse
	// =====================================================================
	private void ValFormaSemAsRedeas()
	{
		GD.Print("[validacoes] -- 15) `forma <id>`: quem perdeu as redeas nao muda de forma pela tecla --");
		Vec2 palco = CorredorLivre(24);
		ServerPlayer saiyajin = ValGente("PossuidoDaBancada", "Saiyan", palco, bp: 5_000_000);
		saiyajin.Forma.Entrar(Catalogo.IdSsj1);
		ValCheca("(montagem) um Saiyajin com escada, em Super Saiyajin", TemEscada(saiyajin) && saiyajin.Forma.Atual == Catalogo.IdSsj1, saiyajin.Forma.Atual);

		AssumirOCorpo(saiyajin, new Jandirus.Core.Ai.Cerebro { VidaCautelosa = 0, Inteligencia = 0 });
		try
		{
			List<string> falas = Ouvir(() => Verbo(saiyajin, "forma", Catalogo.IdBase));
			ValCheca("**O POSSUIDO NAO REVERTE COM `forma base`** (o despacho ja recusava o `C2S.Transformar` de quem perdeu as redeas, e este "
					+ "verbo passava por fora): a forma fica, e o dono le por que",
					SemAsRedeas(saiyajin) && saiyajin.Forma.Atual == Catalogo.IdSsj1 && Disse(falas, "não manda neste corpo"),
					$"forma {saiyajin.Forma.Atual} | {Ultimos(falas)}");

			FormaSemRedeasDeTeste = true;
			try
			{
				List<string> comDefeito = Ouvir(() => Verbo(saiyajin, "forma", Catalogo.IdBase));
				ValCheca("(defeito injetado: o verbo atende quem perdeu as redeas) a prova de cima REPROVA -- o corpo possuido volta ao normal "
						+ "pela mao do dono",
						saiyajin.Forma.Atual == Catalogo.IdBase, $"forma {saiyajin.Forma.Atual} | {Ultimos(comDefeito)}");
			}
			finally { FormaSemRedeasDeTeste = false; }
		}
		finally { DevolverAsRedeas(saiyajin); }

		saiyajin.Forma.Entrar(Catalogo.IdSsj1);
		List<string> dono = Ouvir(() => Verbo(saiyajin, "forma", Catalogo.IdBase));
		ValCheca("O CAMINHO LEGITIMO: com as redeas na mao, a mesma tecla volta ao normal",
				!SemAsRedeas(saiyajin) && saiyajin.Forma.Atual == Catalogo.IdBase, $"forma {saiyajin.Forma.Atual} | {Ultimos(dono)}");
	}

	// =====================================================================
	// 16) a skill emprestada pela fusao
	// =====================================================================
	private void ValEmprestadaPelaFusao()
	{
		GD.Print("[validacoes] -- 16) `skill_esquecer`: o que a fusao emprestou nao devolve marco, nem pelo verbo nem pela cascata --");
		if (_skills == null) { ValCheca("(montagem) ha catalogo de skills", false); return; }

		// Quatro compras de tier 1 no Body (4 marcos -> tier 2) e a Afterimage (tier 2, 2 marcos) EMPRESTADA pela fusao --
		// o arranjo da `--arvoreteste`, com a ultima vindo do passageiro em vez da loja.
		const string treino = "/datum/skill/training", evasao = "/datum/skill/evasive", drills = "/datum/skill/drills",
					 leve = "/datum/skill/qingqong";
		(ServerPlayer Dono, FusaoAtiva Fusao) Fundido(string nome)
		{
			ServerPlayer dono = ValGente(nome, "Human", CorredorLivre(24));
			ServerPlayer passageiro = ValGente(nome + "Passageiro", "Human", dono.Pos + new Vec2(ZoneCollision.TileSize, 0));
			dono.Livro!.Conceder(10);
			AplicarEfeitos(dono);
			foreach (string p in new[] { treino, evasao, drills, leve }) Aprender(dono, p);
			var f = new FusaoAtiva { Dono = dono, Passageiro = passageiro };
			dono.Livro.Dar(PathDoZanzoken);                    // o passo 3 do `Fundir`: `dono.Livro.Dar(path)` + a lista
			f.SkillsEmprestadas.Add(PathDoZanzoken);
			_fundidos[dono.Id] = f;
			AplicarEfeitos(dono);
			return (dono, f);
		}

		(ServerPlayer a, _) = Fundido("FundidoDaBancada");
		try
		{
			int marcos = a.Livro!.MarcosLivres;
			ValCheca("(montagem) um dono de fusao com quatro skills COMPRADAS e a Afterimage EMPRESTADA pelo passageiro (6 marcos livres)",
					a.Livro.Sabe(treino) && a.Livro.Sabe(evasao) && a.Livro.Sabe(drills) && a.Livro.Sabe(leve) && a.Livro.Sabe(PathDoZanzoken)
					&& marcos == 6 && EmprestadaPelaFusao(a, PathDoZanzoken), $"marcos {marcos}");

			List<string> recusa = Ouvir(() => Verbo(a, "skill_esquecer", PathDoZanzoken));
			ValCheca("**O QUE A FUSAO EMPRESTOU NAO SE VENDE**: esquecer a skill do passageiro e recusado -- ela fica no livro e nenhum marco entra",
					a.Livro.Sabe(PathDoZanzoken) && a.Livro.MarcosLivres == marcos && Disse(recusa, "veio com a fusão"),
					$"marcos {marcos}->{a.Livro.MarcosLivres} | {Ultimos(recusa)}");

			// A CASCATA: tres compradas a menos, o Body cai pro tier 1 e a Afterimage (tier 2) deixaria de caber.
			foreach (string p in new[] { leve, drills, evasao }) Ouvir(() => Verbo(a, "skill_esquecer", p));
			ValCheca("**...NEM PELA ARVORE QUE ENCOLHE**: esquecidas tres das compradas (+3 marcos), a emprestada nao cai na cascata e nao "
					+ "paga os 2 marcos dela -- ela sai na separacao, como entrou",
					a.Livro.Sabe(PathDoZanzoken) && a.Livro.MarcosLivres == marcos + 3 && !a.Livro.Sabe(evasao),
					$"sabe {a.Livro.Sabe(PathDoZanzoken)}, marcos {marcos}->{a.Livro.MarcosLivres}");
		}
		finally { _fundidos.Remove(a.Id); }

		// OS DOIS DEFEITOS, UM DE CADA VEZ. Desde a lista de compradas (`SkillBook.FoiComprada`, secao 18) sao duas
		// travas: o VERBO recusa esquecer o que e do passageiro, e o LIVRO nao paga pelo que ninguem comprou.
		(ServerPlayer b, _) = Fundido("FundidoComDefeitoDaBancada");
		EsqueceOEmprestadoDeTeste = true;
		try
		{
			int marcos = b.Livro!.MarcosLivres;
			Ouvir(() => Verbo(b, "skill_esquecer", PathDoZanzoken));
			ValCheca("(defeito injetado: o verbo aceita esquecer a emprestada) a primeira prova REPROVA -- a skill do passageiro sai do "
					+ "livro do dono antes da separacao. Marco, nem assim: o livro sabe que ninguem a comprou",
					!b.Livro.Sabe(PathDoZanzoken) && b.Livro.MarcosLivres == marcos, $"sabe {b.Livro.Sabe(PathDoZanzoken)}, marcos {marcos}->{b.Livro.MarcosLivres}");
		}
		finally { EsqueceOEmprestadoDeTeste = false; _fundidos.Remove(b.Id); }

		(ServerPlayer d, FusaoAtiva fd) = Fundido("FundidoSemListaDaBancada");
		EsqueceOEmprestadoDeTeste = true;
		Jandirus.Core.Skills.SkillBook.TudoContaComoCompradoDeTeste = true;
		try
		{
			int marcos = d.Livro!.MarcosLivres;
			Ouvir(() => Verbo(d, "skill_esquecer", PathDoZanzoken));
			int peloVerbo = d.Livro.MarcosLivres - marcos;
			// a separacao (`Separar`): tira do dono o que a fusao emprestou -- e ja nao ha o que tirar
			foreach (string p in fd.SkillsEmprestadas) d.Livro.Esquecer(p);
			ValCheca("(os dois defeitos: o verbo aceita, e o livro volta a nao saber o que foi comprado) a impressora de antes -- o dono "
					+ "esquece a skill do passageiro, leva 2 marcos de uma compra que nunca fez, e a separacao nao tem mais o que tirar dele",
					peloVerbo == 2 && d.Livro.MarcosLivres == marcos + 2, $"marcos {marcos}->{d.Livro.MarcosLivres}");
		}
		finally { EsqueceOEmprestadoDeTeste = false; Jandirus.Core.Skills.SkillBook.TudoContaComoCompradoDeTeste = false; _fundidos.Remove(d.Id); }

		// A CASCATA, SEM A LISTA: a emprestada volta a cair com a arvore, pagando.
		(ServerPlayer e, _) = Fundido("FundidoNaCascataDaBancada");
		Jandirus.Core.Skills.SkillBook.TudoContaComoCompradoDeTeste = true;
		try
		{
			int marcos = e.Livro!.MarcosLivres;
			foreach (string p in new[] { leve, drills, evasao }) Ouvir(() => Verbo(e, "skill_esquecer", p));
			ValCheca("(defeito injetado: o livro nao sabe o que foi comprado) a prova da CASCATA reprova -- a arvore que encolhe leva a "
					+ "emprestada e paga os 2 marcos dela (3 das compradas + 2 = 5)",
					!e.Livro.Sabe(PathDoZanzoken) && e.Livro.MarcosLivres == marcos + 5, $"sabe {e.Livro.Sabe(PathDoZanzoken)}, marcos {marcos}->{e.Livro.MarcosLivres}");
		}
		finally { Jandirus.Core.Skills.SkillBook.TudoContaComoCompradoDeTeste = false; _fundidos.Remove(e.Id); }

		// O CAMINHO LEGITIMO: sem fusao, a Afterimage COMPRADA devolve os 2 marcos dela.
		ServerPlayer c = ValGente("CompradorDaBancada", "Human", CorredorLivre(24));
		c.Livro!.Conceder(10);
		AplicarEfeitos(c);
		foreach (string p in new[] { treino, evasao, drills, leve, PathDoZanzoken }) Aprender(c, p);
		int antes = c.Livro.MarcosLivres;
		Ouvir(() => Verbo(c, "skill_esquecer", PathDoZanzoken));
		ValCheca("O CAMINHO LEGITIMO: a mesma skill, COMPRADA, se esquece e devolve os 2 marcos",
				antes == 4 && !c.Livro.Sabe(PathDoZanzoken) && c.Livro.MarcosLivres == antes + 2, $"marcos {antes}->{c.Livro.MarcosLivres}");
	}

	// =====================================================================
	// 17) sala_soltar: o Guardiao solta do Templo
	// =====================================================================
	private void ValOGuardiaoNoTemplo(ZoneKey terra)
	{
		GD.Print("[validacoes] -- 17) `sala_soltar`: o Guardiao da Terra destranca a Sala do Tempo DO TEMPLO (dono, 2026-10-11) --");
		const int T = ZoneCollision.TileSize;
		ZoneKey sala = ZoneKey.Premade(ZonaDaSala);
		ZoneKey templo = ZoneKey.Premade(ClasseDeNuvem.ZonaDoTemplo);
		Vec2 palco = CorredorLivre(24);
		Vec2 naPorta = new(124 * T + 16, 81 * T + 16);
		ServerPlayer guardiao = ValGente("GuardiaoDaBancada", "Human", palco);
		ServerPlayer preso = ValGente("PresoDoGuardiaoDaBancada", "Human", palco + new Vec2(2 * T, 0));
		ValMover(preso, sala, new Vec2((EntradaByondX - 1) * T + T / 2f, 345 * T + T / 2f));
		preso.SalaPreso = true;
		_tronos["guardian"] = guardiao.Conta;

		List<string> daTerra = Ouvir(() => Verbo(guardiao, "sala_soltar", preso.Id.ToString()));
		ValCheca("**O GUARDIAO SOLTA DO TEMPLO** (*\"o guardiao tem q estar no templo pra soltar\"*): da Terra o verbo e recusado dizendo "
				+ "ONDE ele tem de estar -- o preso continua preso e nao ouve nada",
				preso.SalaPreso && Disse(daTerra, "Templo Sagrado") && !Disse(daTerra, "destranca a porta da Sala"),
				$"preso {preso.SalaPreso} | {Ultimos(daTerra)}");

		SoltaDeLongeDeTeste = true;
		try
		{
			Ouvir(() => Verbo(guardiao, "sala_soltar", preso.Id.ToString()));
			ValCheca("(defeito injetado: o Guardiao solta de qualquer lugar) a prova de cima REPROVA -- da Terra, a tranca sai", !preso.SalaPreso);
		}
		finally { SoltaDeLongeDeTeste = false; preso.SalaPreso = true; }

		List<string> doAdmin = Ouvir(() => AdminSoltarDaSala(guardiao, preso.Id.ToString()));
		ValCheca("A VALVULA CONTINUA: o ADMIN solta de onde estiver (`admin_sala_soltar`)", !preso.SalaPreso, Ultimos(doAdmin));
		preso.SalaPreso = true;

		ValMover(guardiao, templo, naPorta);
		List<string> doTemplo = Ouvir(() => Verbo(guardiao, "sala_soltar", preso.Id.ToString()));
		ValCheca("O CAMINHO LEGITIMO: no Templo Sagrado, o Guardiao destranca -- o preso ouve, e continua onde estava (soltar nao teleporta)",
				!preso.SalaPreso && NaSala(preso) && Disse(doTemplo, "destranca a porta da Sala"), Ultimos(doTemplo));

		ValMover(guardiao, terra, palco);
		preso.SalaPreso = true;
		_tronos.Remove("guardian");
		List<string> semCargo = Ouvir(() => Verbo(guardiao, "sala_soltar", preso.Id.ToString()));
		ValCheca("...e quem nao e o Guardiao continua ouvindo que so ele solta, antes de qualquer conversa sobre lugar",
				preso.SalaPreso && Disse(semCargo, "só o Guardião da Terra tira"), Ultimos(semCargo));
		preso.SalaPreso = false;
	}
}
