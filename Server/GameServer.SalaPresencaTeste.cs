using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA PRESENCA NA SALA DO TEMPO (`--salapresencateste`): quem esta la dentro de verdade e contado pela
/// sessao -- e ninguem rende os 280x sem a conta de dias e de idade andar.
///
/// O FURO (achado em 2026-10-11, lido no codigo; esta bancada o MEDE pelo caminho de producao): a vaga da Sala da
/// Terra (`_naSala`) saia com a ZONA do corpo, e ha quatro jeitos de trocar de zona sem sair da Sala -- mergulhar
/// na mente (o corpo fica deitado la), virar passageiro de uma fusao, ser absorvido por um Majin, ser selado. Quem
/// voltava ficava na Sala SEM sessao: o tique nao contava dias, idade nem janela, e o funil seguia pagando 280x.
/// E quem entrava de carona (o passageiro de quem atravessou a porta) rendia igual, sem chave e sem recarga.
///
/// HOSPEDANDO, no primeiro tique com um jogador no mundo: o TRANSE precisa de um jogador de verdade (corpo sem
/// `Peer` nao larga o corpo). Os outros personagens sao corpos forjados, pelos moldes da `--validacoesteste`.
///
/// AS CENAS (cada uma pelo caminho de producao: a porta do Templo, o `LargarOCorpo`/`VoltarProCorpo`, a fusao
/// inteira com convite e cena, o `absorver` do Majin, os dois tiques da Sala):
///   A. o TRANSE la dentro, e o acordar;
///   B. o PASSAGEIRO de uma fusao que tinha sessao, e a separacao la dentro;
///   C. a CARONA: o passageiro de quem atravessou a porta, separado la dentro -- e o relog dele;
///   D. FUNDIR durante a janela de 2 minutos;
///   E. ser ABSORVIDO por um Majin durante a janela.
/// Cada cena imprime a MEDIDA crua (dias contados, ritmo, idade, janela, vaga) e guarda o defeito dela.
///
///     Godot --headless --path . --host --rede 8354 --salapresencateste --raca Human --conta bancada_salapresenca --nome Fiscal
/// </summary>
public sealed partial class GameServer
{
	/// <summary>A flag da bancada, lida AQUI (o molde da `--validacoesteste`): quem a dispara e o tique das salas.</summary>
	private static readonly bool PresencaNaSalaDeTeste = Array.IndexOf(OS.GetCmdlineArgs(), "--salapresencateste") >= 0;

	private bool _presencaTestou;
	private int _presencaOk, _presencaFalhou;

	/// <summary>A BANCADA RODA UMA VEZ, no primeiro tique em que ha um jogador com tela (o host).</summary>
	private void DispararABancadaDaPresenca()
	{
		if (_presencaTestou || Jogadores.FirstOrDefault() is not { } host) return;
		_presencaTestou = true;
		GD.Print("[server] BANCADA: a presenca na Sala do Tempo (--salapresencateste)");
		RodarBancadaDaPresencaNaSala(host);
	}

	private void PresencaCheca(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _presencaOk++; GD.Print($"[presenca]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _presencaFalhou++; GD.PrintErr($"[presenca]   FALHA {nome}   [{detalhe}]"); }
	}

	private void PresencaCena(string nome, Action cena)
	{
		try { cena(); }
		catch (Exception e)
		{
			PresencaCheca($"a cena \"{nome}\" correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
	}

	private void RodarBancadaDaPresencaNaSala(ServerPlayer host)
	{
		GD.Print("\n[presenca] ================ QUEM ESTA NA SALA DO TEMPO E CONTADO PELA SESSAO (2026-10-11) ================");
		_presencaOk = _presencaFalhou = 0;

		const int T = ZoneCollision.TileSize;
		const double Cheio = GainKnobs.TimeChamberMult;
		ZoneKey sala = ZoneKey.Premade(ZonaDaSala), lookout = ZoneKey.Premade("Lookout");
		Vec2 naPorta = new(124 * T + 16, 81 * T + 16);
		Vec2 noMeio = new((EntradaByondX - 1) * T + T / 2f, 345 * T + T / 2f);

		// ---- o mundo volta como estava ----
		ZoneKey zonaDoHost = host.Zone;
		Vec2 posDoHost = host.Pos;
		var vagasDeAntes = new Dictionary<string, long>(_naSala, StringComparer.Ordinal);
		(long Entrada, double Dilatacao, string Porta, double Dias, bool Chave, bool Preso, int Idade) doHost =
			(host.SalaUltimaEntrada, host.SalaDilatacao, host.SalaPlaneta, host.SalaDiasDaSessao, host.SalaAutorizada, host.SalaPreso, host.Idade);
		var gente = new List<ServerPlayer> { host };
		_naSala.Clear();

		// ---- os gestos do palco ----
		bool Rende(ServerPlayer p, double ritmo) => Math.Abs(p.Ficha.zoneGainMult - ritmo) < 1e-6;
		void Mover(ServerPlayer p, ZoneKey z, Vec2 onde)
		{
			if (p.Peer != null) MoveToZone(p.Id, z, onde);
			else { ValMover(p, z, onde); AplicarGravidade(p); }
			p.Moving = false;
			p.Voando = false;
		}
		void PelaPorta(ServerPlayer p)
		{
			Mover(p, lookout, naPorta);
			p.Ficha.dead = p.Ficha.KO = false;
			p.SalaAutorizada = true;
			p.SalaUltimaEntrada = 0;
			p.SalaPreso = false;
			p.SalaJanelaAte = 0;
			ComandoDaSalaDoTempo(p, "sala_entrar", "");
		}
		// UM MINUTO REAL DE TIQUE POR VOLTA: 24 voltas sao um dia in-game. Os dois tiques da Sala, na ordem do jogo.
		void Minutos(int n)
		{
			for (int i = 0; i < n; i++) { TickDaSalaDoTempo(); TickDaSessaoDaSala(60); }
		}
		void ProntosPraFundir(ServerPlayer a, ServerPlayer b, ZoneKey z, Vec2 onde)
		{
			foreach (ServerPlayer p in new[] { a, b })
			{
				if (_emCenaDeFusao.GetValueOrDefault(p.Id) is { } c) SoltarDaCenaDeFusao(c);
				_pedidosDeFusao.Remove(p.Id);
				p.Ficha.fusion_cooldown_until = 0;
				p.Combate.Stun = 0;
				p.CenaSegundos = 0;
			}
			Mover(a, z, onde);
			Mover(b, z, onde + new Vec2(T, 0));
		}
		// TODO MUNDO PRA FORA, DE VERDADE: sem fusao, sem transe, sem absorvido, sem tranca -- e a Sala vazia.
		void Esvaziar()
		{
			foreach (ServerPlayer p in gente)
			{
				if (FusaoDe(p.Id) is { } f) Separar(f, "fim da cena");
				CuspirTodosDoMajin(p.Id, "");
			}
			foreach (ServerPlayer p in gente)
			{
				if (p.BonecoLargado != null) VoltarProCorpo(p, "fim da cena");
				p.SalaPreso = false;
				p.SalaJanelaAte = 0;
				p.Ficha.dead = p.Ficha.KO = false;
				_prontoAbsorver.Remove(p.Id);
				Mover(p, lookout, naPorta);
			}
			TickDaSalaDoTempo();
			_naSala.Clear();
		}
		string Medida(ServerPlayer p) =>
			$"{p.SalaDiasDaSessao:0.###} dia(s) contados, ritmo {p.Ficha.zoneGainMult:0.##}x, idade {p.Idade}, "
			+ $"janela {(p.SalaJanelaAte > 0 ? "armada" : "nao armada")}, preso {p.SalaPreso}, "
			+ $"vaga {(p.Assinatura.Length > 0 && _naSala.ContainsKey(p.Assinatura))}, lotacao {QuantosNaSala()}";

		try
		{
			_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
			Vec2 palco = CorredorLivre(24);
			ServerPlayer dono = ValGente("DonoDaPresenca", "Human", palco, bp: 500_000);
			ServerPlayer passageiro = ValGente("PassageiroDaPresenca", "Human", palco + new Vec2(T, 0), bp: 500_000);
			ServerPlayer carona = ValGente("CaronaDaPresenca", "Human", palco + new Vec2(2 * T, 0), bp: 500_000);
			ServerPlayer majin = ValGente("MajinDaPresenca", "Majin", palco + new Vec2(3 * T, 0), bp: 1_000_000, skills: [AbsorcaoMajin.Skill]);
			ServerPlayer vitima = ValGente("VitimaDaPresenca", "Human", palco + new Vec2(4 * T, 0), bp: 400_000);
			gente.AddRange([dono, passageiro, carona, majin, vitima]);
			foreach (ServerPlayer p in gente) { p.Idade = 25; p.Ficha.Idade = 25; }

			// =====================================================================
			// A) O TRANSE LA DENTRO
			// =====================================================================
			void OTranse(bool defeito)
			{
				Esvaziar();
				PelaPorta(host);
				bool entrou = NaSala(host) && _naSala.ContainsKey(host.Assinatura) && Rende(host, Cheio);
				if (!defeito) PresencaCheca("(montagem) o host atravessou a porta do Templo: esta na Sala, com vaga e rendendo 280x", entrou, Medida(host));
				int idadeNaEntrada = host.Idade;
				Minutos(12);   // meio dia in-game
				double diasAntes = host.SalaDiasDaSessao;

				bool largou = LargarOCorpo(host, DimensaoMental.De(host.Id), DimensaoMental.PixelDe(DimensaoMental.CelDeQuemMedita));
				Minutos(6);
				if (!defeito)
					PresencaCheca("NA MENTE (o corpo fica deitado na Sala): a vaga continua dele, a conta de dias PARA, e la ele nao rende os 280x",
							largou && NaMente(host) && _naSala.ContainsKey(host.Assinatura) && QuantosNaSala() == 1
							&& Math.Abs(host.SalaDiasDaSessao - diasAntes) < 1e-9 && host.Ficha.zoneGainMult < 1.0001, Medida(host));

				VoltarProCorpo(host, "o transe da bancada acabou");
				bool voltou = NaSala(host);
				double ritmoAoAcordar = host.Ficha.zoneGainMult;
				Minutos(72);   // TRES dias in-game: um a mais que a sessao inteira
				bool janela = host.SalaJanelaAte > 0 && !host.SalaPreso;
				GD.Print($"[presenca]   (medida{(defeito ? ", com o defeito" : "")}) acordou com {diasAntes:0.###} dia(s) gastos e ritmo {ritmoAoAcordar:0.##}x; "
						 + $"3 dias in-game de tique depois: {Medida(host)}");

				if (defeito)
				{
					PresencaCheca("(os dois defeitos: a vaga sai com a ZONA do corpo, e sem sessao ainda se rende -- o jogo de antes) a regua do transe REPROVA -- acordado, ele rende 280x por tres dias "
							+ "sem a conta andar um minuto, sem envelhecer, sem janela e sem vaga",
							voltou && Math.Abs(host.SalaDiasDaSessao - diasAntes) < 1e-9 && Rende(host, Cheio) && host.Idade == idadeNaEntrada
							&& host.SalaJanelaAte == 0 && !host.SalaPreso && QuantosNaSala() == 0, Medida(host));
					return;
				}

				PresencaCheca("**QUEM ACORDA DO TRANSE NA SALA VOLTA A SER CONTADO**: a sessao continua de onde parou, fecha os 2 dias, envelhece 2 anos, "
						+ "e os 280x acabam com ela",
						voltou && Math.Abs(ritmoAoAcordar - Cheio) < 1e-6 && !SalaDoTempo.SessaoValendo(host.SalaDiasDaSessao)
						&& host.Idade - idadeNaEntrada == 2 && Rende(host, 1), Medida(host));
				host.SalaJanelaAte = NowMs() - 1;
				TickDaSessaoDaSala(1);
				PresencaCheca("...e a JANELA e a TRANCA valem pra ele: fechados os 2 dias a janela arma, e vencida a janela ele fica PRESO",
						janela && host.SalaPreso, Medida(host));
			}
			PresencaCena("A", () =>
			{
				GD.Print("[presenca] -- A) o transe la dentro: mergulhar na propria mente, e acordar --");
				OTranse(defeito: false);
				VagaSaiNoBolsoDeTeste = SemSessaoTambemRendeDeTeste = true;   // os DOIS: e o jogo de antes (a vaga sai com a zona, e quem fica sem sessao ainda rende)
				try { OTranse(defeito: true); }
				finally { VagaSaiNoBolsoDeTeste = SemSessaoTambemRendeDeTeste = false; }
			});

			// =====================================================================
			// B) O PASSAGEIRO QUE TINHA SESSAO
			// =====================================================================
			void OPassageiro(bool defeito)
			{
				Esvaziar();
				PelaPorta(dono);
				PelaPorta(passageiro);
				Minutos(6);   // um quarto de dia
				ProntosPraFundir(dono, passageiro, sala, noMeio);
				FusaoAtiva? f = FundirDeVerdade(dono, passageiro, TipoDeFusao.Potara);
				bool fundiu = f != null && f.Dono == dono && f.Passageiro == passageiro && EhOSelo(passageiro.Zone);
				double diasDoPassageiro = passageiro.SalaDiasDaSessao;
				Minutos(6);
				if (!defeito)
					PresencaCheca("PASSAGEIRO DE UMA FUSAO LA DENTRO: a vaga continua dele (a lotacao segue em 2), a conta dele PARA, e a do dono anda",
							fundiu && _naSala.ContainsKey(passageiro.Assinatura) && QuantosNaSala() == 2
							&& Math.Abs(passageiro.SalaDiasDaSessao - diasDoPassageiro) < 1e-9 && dono.SalaDiasDaSessao > diasDoPassageiro + 0.2,
							$"fundiu {fundiu} | passageiro: {Medida(passageiro)}");

				if (f != null) Separar(f, "a fusao acaba dentro da Sala");
				bool naSala = NaSala(passageiro);
				Minutos(72);
				GD.Print($"[presenca]   (medida{(defeito ? ", com o defeito" : "")}) o passageiro tinha {diasDoPassageiro:0.###} dia(s) ao fundir; "
						 + $"separado la dentro, 3 dias in-game de tique depois: {Medida(passageiro)}");

				if (defeito)
				{
					PresencaCheca("(os dois defeitos: a vaga sai com a ZONA do corpo, e sem sessao ainda se rende -- o jogo de antes) a regua do passageiro REPROVA -- separado, ele rende 280x sem a conta andar",
							fundiu && naSala && Math.Abs(passageiro.SalaDiasDaSessao - diasDoPassageiro) < 1e-9 && Rende(passageiro, Cheio)
							&& !passageiro.SalaPreso && passageiro.SalaJanelaAte == 0, Medida(passageiro));
					return;
				}
				PresencaCheca("**SEPARADO LA DENTRO, O PASSAGEIRO VOLTA A SER CONTADO**: a sessao dele continua, fecha os 2 dias e os 280x acabam -- com a janela armada",
						fundiu && naSala && !SalaDoTempo.SessaoValendo(passageiro.SalaDiasDaSessao) && Rende(passageiro, 1)
						&& (passageiro.SalaJanelaAte > 0 || passageiro.SalaPreso) && QuantosNaSala() == 2, Medida(passageiro));
			}
			PresencaCena("B", () =>
			{
				GD.Print("[presenca] -- B) o passageiro de uma fusao que tinha sessao: fundir la dentro, e separar la dentro --");
				OPassageiro(defeito: false);
				VagaSaiNoBolsoDeTeste = SemSessaoTambemRendeDeTeste = true;   // os DOIS: e o jogo de antes (a vaga sai com a zona, e quem fica sem sessao ainda rende)
				try { OPassageiro(defeito: true); }
				finally { VagaSaiNoBolsoDeTeste = SemSessaoTambemRendeDeTeste = false; }
			});

			// =====================================================================
			// C) A CARONA
			// =====================================================================
			PresencaCena("C", () =>
			{
				GD.Print("[presenca] -- C) a carona: o passageiro de quem atravessou a porta, separado la dentro -- e o relog dele --");
				Esvaziar();
				carona.SalaUltimaEntrada = 0;
				carona.SalaDiasDaSessao = 0;
				carona.SalaAutorizada = false;
				// A FUSAO NASCE LA FORA (na Terra), e so o DONO vai ate a porta: o passageiro viaja dentro dele.
				ProntosPraFundir(dono, carona, ZonaDaBancadaDeProjetil, palco);
				FusaoAtiva? f = FundirDeVerdade(dono, carona, TipoDeFusao.Potara);
				Mover(dono, lookout, naPorta);
				dono.SalaAutorizada = true;
				dono.SalaUltimaEntrada = 0;
				ComandoDaSalaDoTempo(dono, "sala_entrar", "");
				bool entrouFundido = f != null && NaSala(dono) && EhOSelo(carona.Zone);
				if (f != null) Separar(f, "a fusao acaba dentro da Sala");
				bool dentro = NaSala(carona);
				double ritmoAoSeparar = carona.Ficha.zoneGainMult;
				Minutos(72);
				GD.Print($"[presenca]   (medida) a carona nunca atravessou a porta (sem chave, recarga {carona.SalaUltimaEntrada}); separada la dentro "
						 + $"com ritmo {ritmoAoSeparar:0.##}x, 3 dias in-game de tique depois: {Medida(carona)}");
				PresencaCheca("**QUEM ENTROU DE CARONA NAO TEM SESSAO E NAO RENDE**: ritmo 1x do comeco ao fim, sem chave gasta nem recarga armada -- e sem "
						+ "prisao: ele nao tem sessao pra vencer",
						entrouFundido && dentro && Math.Abs(ritmoAoSeparar - 1) < 1e-6 && Rende(carona, 1) && !carona.SalaPreso
						&& FaseDaSalaPraTela(carona) == 0 && carona.SalaUltimaEntrada == 0, Medida(carona));

				SemSessaoTambemRendeDeTeste = true;
				try
				{
					AplicarGravidade(carona);
					Minutos(72);
					PresencaCheca("(defeito injetado: quem esta la dentro sem sessao tambem rende) a regua da carona REPROVA -- 280x por tres dias sem um dia contado",
							Rende(carona, Cheio) && carona.SalaDiasDaSessao == 0 && !carona.SalaPreso, Medida(carona));
				}
				finally { SemSessaoTambemRendeDeTeste = false; AplicarGravidade(carona); }

				// ---- o relog la dentro ----
				SalaDoTempoNoLogin(carona);
				AplicarGravidade(carona);
				Minutos(6);
				PresencaCheca("RELOGAR LA DENTRO NAO DA SESSAO A QUEM NAO ATRAVESSOU A PORTA: a carona continua sem vaga, sem conta e em 1x",
						!_naSala.ContainsKey(carona.Assinatura) && Rende(carona, 1) && carona.SalaDiasDaSessao == 0, Medida(carona));
				LoginDaVagaSemPortaDeTeste = true;
				try
				{
					SalaDoTempoNoLogin(carona);
					AplicarGravidade(carona);
					Minutos(6);
					PresencaCheca("(defeito injetado: o login la dentro da vaga a qualquer um) a regua do relog REPROVA -- a carona ganha uma sessao inteira, "
							+ "sem chave e sem recarga", _naSala.ContainsKey(carona.Assinatura) && Rende(carona, Cheio), Medida(carona));
				}
				finally { LoginDaVagaSemPortaDeTeste = false; }

				// ...e quem ATRAVESSOU a porta volta do relog com a sessao dele (o reinicio esvazia a lista, que e memoria)
				dono.SalaDiasDaSessao = 0.5;   // (palco) a sessao dele ja tinha fechado nas voltas de cima: meio dia gasto, pra a conta ter pra onde andar
				dono.SalaJanelaAte = 0;
				dono.SalaPreso = false;
				double diasDoDono = dono.SalaDiasDaSessao;
				_naSala.Clear();
				SalaDoTempoNoLogin(dono);
				AplicarGravidade(dono);
				Minutos(6);
				PresencaCheca("...e quem ATRAVESSOU a porta volta do relog com a vaga e a sessao dele, contando de onde parou",
						_naSala.ContainsKey(dono.Assinatura) && dono.SalaDiasDaSessao > diasDoDono, Medida(dono));
			});

			// =====================================================================
			// D) FUNDIR DURANTE A JANELA
			// =====================================================================
			void NaJanela(bool defeito)
			{
				Esvaziar();
				PelaPorta(dono);
				PelaPorta(passageiro);
				passageiro.SalaDiasDaSessao = SalaDoTempo.SessaoEmDias;
				TickDaSessaoDaSala(1);   // a sessao dele acabou: este tique ARMA a janela
				bool janela = passageiro.SalaJanelaAte > 0 && !passageiro.SalaPreso;
				ProntosPraFundir(dono, passageiro, sala, noMeio);
				FusaoAtiva? f = FundirDeVerdade(dono, passageiro, TipoDeFusao.Potara);
				bool fundiu = f != null && EhOSelo(passageiro.Zone);
				TickDaSalaDoTempo();
				passageiro.SalaJanelaAte = NowMs() - 1;   // os 2 minutos vencem com ele DENTRO do outro
				TickDaSessaoDaSala(1);
				TickDaSalaDoTempo();
				GD.Print($"[presenca]   (medida{(defeito ? ", com o defeito" : "")}) fundiu na janela ({fundiu}); vencida a janela, o passageiro: {Medida(passageiro)}");

				if (defeito)
				{
					PresencaCheca("(defeito injetado: a tranca nao anda no bolso) a regua da janela REPROVA -- vencidos os 2 minutos ele NAO esta preso",
							janela && fundiu && !passageiro.SalaPreso, Medida(passageiro));
					return;
				}
				PresencaCheca("**FUNDIR DURANTE A JANELA NAO ESCAPA DA TRANCA**: vencidos os 2 minutos o passageiro esta PRESO dentro do outro, a vaga "
						+ "continua dele (lotacao 2), e quem o carrega nao sai da Sala com ele",
						janela && fundiu && passageiro.SalaPreso && _naSala.ContainsKey(passageiro.Assinatura) && QuantosNaSala() == 2
						&& PresoQueEsteCorpoCarrega(dono) == passageiro, Medida(passageiro));
				if (f != null) Separar(f, "a fusao acaba dentro da Sala");
				Minutos(3);
				PresencaCheca("...e separado la dentro ele esta na Sala, PRESO e contado na lotacao",
						NaSala(passageiro) && passageiro.SalaPreso && QuantosNaSala() == 2, Medida(passageiro));
			}
			PresencaCena("D", () =>
			{
				GD.Print("[presenca] -- D) fundir durante a janela de 2 minutos --");
				NaJanela(defeito: false);
				TrancaNaoFechaNoBolsoDeTeste = true;
				try { NaJanela(defeito: true); }
				finally { TrancaNaoFechaNoBolsoDeTeste = false; }
			});

			// =====================================================================
			// E) SER ABSORVIDO DURANTE A JANELA
			// =====================================================================
			PresencaCena("E", () =>
			{
				GD.Print("[presenca] -- E) ser absorvido por um Majin durante a janela --");
				Esvaziar();
				PelaPorta(majin);
				PelaPorta(vitima);
				Mover(majin, sala, noMeio + new Vec2(0, 3 * T));
				Mover(vitima, sala, noMeio + new Vec2(T, 3 * T));
				vitima.SalaDiasDaSessao = SalaDoTempo.SessaoEmDias;
				TickDaSessaoDaSala(1);
				bool janela = vitima.SalaJanelaAte > 0 && !vitima.SalaPreso;
				if (!vitima.Ficha.KO) vitima.Combate.Nocautear(600);
				_prontoAbsorver.Remove(majin.Id);
				UsarHabilidade(majin, "absorver");
				bool selou = DentroDeUmMajin(vitima) && AbsorvidosPor(majin.Id).Any(a => a.Quem == vitima.Id);
				TickDaSalaDoTempo();
				vitima.SalaJanelaAte = NowMs() - 1;
				TickDaSessaoDaSala(1);
				TickDaSalaDoTempo();
				GD.Print($"[presenca]   (medida) absorvida na janela ({selou}); vencida a janela, a vitima: {Medida(vitima)}");
				PresencaCheca("**SER ABSORVIDO DURANTE A JANELA TAMBEM NAO ESCAPA**: selada no Majin, a tranca fecha nela e a vaga continua dela (lotacao 2)",
						janela && selou && vitima.SalaPreso && _naSala.ContainsKey(vitima.Assinatura) && QuantosNaSala() == 2, Medida(vitima));
				CuspirTodosDoMajin(majin.Id, "");
				Minutos(3);
				PresencaCheca("...e cuspida la dentro ela esta na Sala, PRESA", NaSala(vitima) && vitima.SalaPreso && QuantosNaSala() == 2, Medida(vitima));
			});

			// =====================================================================
			// F) SAIR DE VERDADE CONTINUA SAINDO
			// =====================================================================
			PresencaCena("F", () =>
			{
				GD.Print("[presenca] -- F) o caminho legitimo: sair da Sala de verdade solta a vaga e fecha a sessao --");
				Esvaziar();
				PelaPorta(dono);
				PelaPorta(passageiro);
				Minutos(6);
				ProntosPraFundir(dono, passageiro, sala, noMeio);
				FusaoAtiva? f = FundirDeVerdade(dono, passageiro, TipoDeFusao.Potara);
				Mover(dono, lookout, naPorta);   // o dono SAI da Sala, com o passageiro dentro
				Minutos(3);
				PresencaCheca("o dono que SAI da Sala com o passageiro dentro leva os dois: as duas vagas voltam, e la fora ninguem rende",
						f != null && QuantosNaSala() == 0 && Rende(dono, 1), $"dono: {Medida(dono)} | passageiro: {Medida(passageiro)}");
				if (f != null) Separar(f, "a fusao acaba fora da Sala");
				Minutos(3);
				PresencaCheca("...e separado LA FORA o passageiro nao volta a ter sessao (so a porta abre outra)",
						!NaSala(passageiro) && Rende(passageiro, 1) && QuantosNaSala() == 0, Medida(passageiro));
			});
		}
		catch (Exception e)
		{
			PresencaCheca($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			VagaSaiNoBolsoDeTeste = TrancaNaoFechaNoBolsoDeTeste = LoginDaVagaSemPortaDeTeste = false;
			SemSessaoTambemRendeDeTeste = false;
			try { Esvaziar(); } catch (Exception e) { GD.PrintErr($"[presenca] a faxina estourou: {e.Message}"); }

			(host.SalaUltimaEntrada, host.SalaDilatacao, host.SalaPlaneta, host.SalaDiasDaSessao, host.SalaAutorizada, host.SalaPreso) =
				(doHost.Entrada, doHost.Dilatacao, doHost.Porta, doHost.Dias, doHost.Chave, doHost.Preso);
			host.Idade = doHost.Idade;
			host.Ficha.Idade = doHost.Idade;
			host.Ficha.Statify();
			host.SigAtributos = "";
			if (host.Zone.Hash == zonaDoHost.Hash) CravarPosicao(host, posDoHost);
			else MoveToZone(host.Id, zonaDoHost, posDoHost);

			foreach (int id in _fundidos.Keys.Where(k => k >= IdBaseDeProjetil).ToList()) _fundidos.Remove(id);
			foreach (ServerPlayer c in _players.Values)
				if (c.Id >= IdBaseDeProjetil)
					foreach (List<ServerPlayer> lista in _zones.Values) lista.Remove(c);
			LimparTudoDaBancada();
			_naSala.Clear();
			foreach (var kv in vagasDeAntes) _naSala[kv.Key] = kv.Value;

			GD.Print($"[presenca] ================ {_presencaOk} OK, {_presencaFalhou} FALHA(S) ================\n");
		}
	}
}
