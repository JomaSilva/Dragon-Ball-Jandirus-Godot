using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA AO VIVO DO CORPO DA IA (`--iateste`).
///
/// ============================ O QUE ELA MEDE, E POR QUE UM TESTE DE MESA NAO MEDIRIA ============================
/// O cerebro e Core puro e o `Comando` que ele emite da pra conferir numa tabela. Nada disso prova
/// o que esta camada promete, que e uma frase so: **a IA paga o mesmo preco que o jogador**.
///
/// Um teste de mesa ve o comando "voar" e da verde. So o servidor ao vivo ve o Ki DESCENDO na conta
/// certa -- e e no caminho entre os dois que mora o atalho: `npc.Voando = true` emite o mesmo
/// comando, produz o mesmo boneco na tela e nao cobra nada.
///
/// Entao a bancada mede DUAS coisas por gesto:
///   1. o gesto ACONTECEU (o corpo voa / apara / carrega / transforma);
///   2. o corpo PAGOU (o Ki caiu exatamente o que a formula do jogador manda).
///
/// A conferencia (1) sozinha e a que este projeto ja viu passar verde com o sistema quebrado -- foi
/// o caso das quatro falhas visuais que atravessaram 4000 checagens ("uniform escrito != pixel
/// desenhado"). Aqui o analogo e "comando emitido != preco cobrado".
/// ==========================================================================================================
///
///     Godot --headless -- --server --iateste
/// </summary>
public partial class GameServer
{
	private bool _iaDeTeste;

	/// <summary>Tolerancia relativa das conferencias de custo. 2% cobre o arredondamento do dt.</summary>
	private const double Folga = 0.02;

	/// <summary>Roda uma vez, no primeiro login. MEXE no mundo (spawna e remove corpos).</summary>
	private void RodarBancadaDeIa(ServerPlayer quem)
	{
		GD.Print("\n===== BANCADA AO VIVO DO CORPO DA IA =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// zona vazia: os anuncios (transformacao, aviso) nao chegam a tela de ninguem
		ZoneKey zona = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		var nascidos = new List<ServerPlayer>();

		/// corpo de bancada: nasce pelo MESMO `PorNoMundo` de todo corpo sem dono
		ServerPlayer Forjar(string nome, double bp, Vec2 pos, bool comCerebro = true)
		{
			var f = new Jandirus.Core.Stats.Fighter
			{
				Name = nome, Race = "Saiyan", BP = bp,
				physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
				kiskill = 5, speed = 5, magiskill = 5, Idade = 25,
				maxstamina = 100, stamina = 100,
			};
			f.Statify();

			// ============================ O `expressedBP` NAO SAI DO `Statify` ============================
			// Ele sai do `PowerLevel` (`Fighter.Power.cs:107`), que num corpo de verdade roda no
			// `TickFichas`. Sem esta linha o corpo forjado nasce com poder EXPRESSO zero -- e ai a
			// `RazaoDePoder` da percepcao vira 1 (parelho) por falta de dado, e o NPC nunca acha que
			// esta em desvantagem. A primeira rodada desta bancada reprovou exatamente assim: "contra
			// um alvo muito mais forte, ela SOBE a escada" falhou com `plano=Pressionar`, e a causa
			// nao era a decisao -- era o corpo de teste nao ter poder nenhum pra comparar.
			// =========================================================================================
			f.PowerLevel();
			f.Ki = f.MaxKi;

			var c = new ServerPlayer
			{
				Id = _nextId++, Peer = null, Name = nome, Zone = zona, Pos = pos,
				Race = "Saiyan", Class = "", Genero = "Male", Idade = 25,
				LastInputMs = NowMs(), Ficha = f,
				Livro = new Jandirus.Core.Skills.SkillBook(),
				Cerebro = comCerebro ? new Cerebro() : null,
			};
			PorNoMundo(c);
			nascidos.Add(c);
			return c;
		}

		/// a skill que abre o voo -- CONCEDIDA, como o `--vooteste` faz. O que a bancada tem que
		/// atravessar e o `AlternarVoo` e o dreno, nao a compra da skill.
		static void EnsinarAVoar(ServerPlayer c)
		{
			c.Livro.Dar("/datum/skill/mind/Ki_Unlocked");
			c.Niveis.Por("/datum/skill/mind/Ki_Unlocked", MaestriaQueDestravaVoo);
		}

		/// N tiques de servidor no corpo dirigido, pelo caminho de producao inteiro
		void Tiques(int n, params ServerPlayer[] tambem)
		{
			for (int i = 0; i < n; i++)
			{
				TickCombate(Protocol.TickSeconds);
				foreach (ServerPlayer c in tambem) TickDoVoo(c, Protocol.TickSeconds);
				foreach (ServerPlayer c in tambem) TickDaCarga(c, Protocol.TickSeconds);
				TickDosCorposSemDono(Protocol.TickSeconds);
			}
		}

		try
		{
			// =====================================================================
			// 1. SEM ATALHO: O VOO COBRA O MESMO QUE COBRA DO JOGADOR
			// =====================================================================
			{
				ServerPlayer npc = Forjar("ia: voo", 50_000, new Vec2(0, 0));
				EnsinarAVoar(npc);
				npc.Cerebro!.Poderes = LerCapacidades(npc);

				Checa("a capacidade de voar vem do MESMO `PodeVoar` do jogador",
					  npc.Cerebro.Poderes.PodeVoar);

				// DECOLAGEM: o preco tem que ser exatamente o `Voo.CustoParaLigar`.
				double antes = npc.Ficha.Ki;
				double esperado = Voo.CustoParaLigar(Voo.HabilidadeNivel1, false);
				AplicarComando(npc, new Comando { AlternarVoo = true }, Protocol.TickSeconds);

				Checa("o comando de voo LEVANTA o corpo (pelo `AlternarVoo`, o mesmo verbo `Fly`)",
					  npc.Voando);
				Checa("...e a decolagem COBRA o `Voo.CustoParaLigar` -- nem um Ki a menos",
					  Math.Abs(antes - npc.Ficha.Ki - esperado) < 0.01,
					  $"pagou {antes - npc.Ficha.Ki:0.###}, devia pagar {esperado:0.###}");

				// DRENO POR SEGUNDO: 30 tiques = 1 s de voo.
				double kiAntes = npc.Ficha.Ki;
				float altAntes = npc.Altitude;
				for (int i = 0; i < 30; i++) TickDoVoo(npc, Protocol.TickSeconds);
				double gasto = kiAntes - npc.Ficha.Ki;
				double devia = Voo.CustoPorSegundo(npc.Ficha.flightability, false) * 30 * Protocol.TickSeconds;

				Checa("um segundo no ar custa o `Voo.CustoPorSegundo` (a MESMA formula do jogador)",
					  Math.Abs(gasto - devia) < devia * Folga + 0.01,
					  $"gastou {gasto:0.###}, formula da {devia:0.###}");
				Checa("...e sem pedir nada o corpo PAIRA (sobe sozinho ate a altura de pairar)",
					  npc.Altitude > altAntes && npc.Altitude <= Voo.AlturaDePairar + 0.01,
					  $"{npc.Altitude:0.#} px");
			}

			// =====================================================================
			// 2. A ALTITUDE ACOMPANHA O ALVO -- o pedido literal do dono
			// =====================================================================
			{
				ServerPlayer npc = Forjar("ia: sobe", 50_000, new Vec2(0, 512));
				ServerPlayer alvo = Forjar("alvo: alto", 50_000, new Vec2(96, 512), comCerebro: false);
				EnsinarAVoar(npc);
				npc.Cerebro!.Poderes = LerCapacidades(npc);
				npc.Cerebro.Inteligencia = 0.9;

				// ============================ O ALVO TAMBEM PAGA PRA VOAR ============================
				// Na primeira rodada esta checagem reprovou com "alvo no 0": o alvo estava voando com
				// `flightability` de quem NAO sabe voar (a sentinela 1), o que custa 175 de Ki por
				// segundo -- ele caiu do ceu em menos de dois segundos, e o NPC acompanhou
				// corretamente uma altura que tinha ido pro chao.
				//
				// A correcao NAO e isentar o alvo do custo (isso mediria um voo que nao existe): e
				// ensina-lo a voar, e manter o tanque dele cheio a cada volta. O que esta sendo medido
				// aqui e a IA acompanhar uma altura, e nao o folego de quem esta sendo perseguido.
				// =================================================================================
				EnsinarAVoar(alvo);
				alvo.Ficha.flightability = Voo.HabilidadeNivel2;
				alvo.Voando = true;
				alvo.Altitude = Voo.AlturaMaxima / Voo.Andares * 1.5f;

				void SegurarOAlvoNoAr()
				{
					alvo.Ficha.Ki = alvo.Ficha.MaxKi;
					alvo.Voando = true;
				}

				Checa("de inicio ele NAO alcanca o alvo (andar 0 contra andar 2)",
					  !Voo.PodeAcertar(Voo.Andar(npc.Altitude), Voo.Andar(alvo.Altitude)),
					  $"andares {Voo.Andar(npc.Altitude)} e {Voo.Andar(alvo.Altitude)}");

				for (int i = 0; i < 150; i++) { SegurarOAlvoNoAr(); Tiques(1, npc, alvo); }   // 5 s

				Checa("ele DECOLOU sozinho pra alcancar (a receita de alcance)", npc.Voando);
				Checa("...e subiu ate o MESMO ANDAR do alvo",
					  Voo.Andar(npc.Altitude) == Voo.Andar(alvo.Altitude),
					  $"ele no {Voo.Andar(npc.Altitude)}, alvo no {Voo.Andar(alvo.Altitude)}");
				Checa("...e agora ALCANCA",
					  Voo.PodeAcertar(Voo.Andar(npc.Altitude), Voo.Andar(alvo.Altitude)));

				// SEM TREMIDA: o alvo parado nao pode produzir um corpo subindo e descendo.
				int trocas = 0;
				bool antesSubir = npc.QuerSubir, antesDescer = npc.QuerDescer;
				for (int i = 0; i < 120; i++)
				{
					SegurarOAlvoNoAr();
					npc.Ficha.Ki = npc.Ficha.MaxKi;   // e o dele tambem: aqui se mede a TREMIDA, nao o folego
					Tiques(1, npc, alvo);
					if (npc.QuerSubir != antesSubir || npc.QuerDescer != antesDescer) trocas++;
					antesSubir = npc.QuerSubir; antesDescer = npc.QuerDescer;
				}
				Checa("...e ele NAO chacoalha na fronteira do andar (a zona morta de altura)",
					  trocas <= 4, $"{trocas} inversoes de subir/descer em 4 s pairando");

				// E O ALVO DESCE: a IA tem que acompanhar pra BAIXO tambem.
				alvo.Altitude = 0f; alvo.Voando = false;
				Tiques(200, npc, alvo);
				Checa("quando o alvo POUSA, ela desce atras dele (rasante, porque e esperta)",
					  Voo.Andar(npc.Altitude) <= 1,
					  $"ficou no andar {Voo.Andar(npc.Altitude)} ({npc.Altitude:0.#} px)");
			}

			// =====================================================================
			// 3. QUEM NAO SABE VOAR NUNCA PEDE PRA VOAR
			// =====================================================================
			// O dono foi explicito: so voa *"se ela tiver a capacidade de voar"*. 10 mil percepcoes
			// sorteadas, com o alvo em toda altura possivel -- se UMA delas produzir `AlternarVoo`,
			// o portao nao esta no lugar.
			{
				var cerebro = new Cerebro { Inteligencia = 1.0, Poderes = new Capacidades { PodeVoar = false } };
				var rng = new Random(20260812);
				int pediu = 0;
				for (int i = 0; i < 10_000; i++)
				{
					var p = new Percepcao
					{
						TemAlvo = true,
						Minha = Vec2.Zero,
						DoAlvo = new Vec2((float)(rng.NextDouble() * 600 - 300), 0),
						AltitudeDoAlvo = (float)(rng.NextDouble() * Voo.AlturaMaxima),
						AlvoVoando = rng.Next(2) == 0,
						MinhaAltitude = 0,
						EstouVoando = false,
						VidaFrac = rng.NextDouble(),
						KiFrac = rng.NextDouble(),
						Ki = rng.NextDouble() * 500,
						FolegoFrac = rng.NextDouble(),
						MeuPoder = 1000, PoderDoAlvo = rng.NextDouble() * 5000,
					};
					if (cerebro.Pensar(p, Protocol.TickSeconds, rng).AlternarVoo) pediu++;
				}
				Checa("sem a capacidade de voar, 10.000 percepcoes NAO produzem um pedido de voo",
					  pediu == 0, $"{pediu} pedidos");
			}

			// =====================================================================
			// 4. A GUARDA: acontece, custa, e NAO e perfeita
			// =====================================================================
			{
				ServerPlayer npc = Forjar("ia: guarda", 50_000, new Vec2(0, 1024));
				ServerPlayer bate = Forjar("agressor", 50_000, new Vec2(40, 1024), comCerebro: false);
				npc.Cerebro!.Poderes = LerCapacidades(npc);
				npc.Cerebro.Disciplina = 1.0;   // maxima: ainda assim nao apara sempre
				bate.Combate.Letal = false;

				// ============================ O AGRESSOR PRECISA ESTAR MIRANDO ============================
				// Na primeira rodada esta secao reprovou com "0 de 34 golpes": o agressor socava o AR.
				// `AlvoNaFrente` so acha quem esta no cone da frente, e o `Facing` padrao apontava pro
				// outro lado. Marcar o alvo (`AlvoId`) e o gesto do JOGADOR pra isso -- o `Atacar` vira
				// o corpo pro marcado antes de arrancar --, e sem golpe chegando o cerebro nao tinha
				// ritmo nenhum pra aprender: o sentido dele e apanhar.
				// ====================================================================================
				bate.AlvoId = npc.Id;

				Checa("ele TEM com que aparar (a mesma pergunta do `EscolherGuarda`)",
					  npc.Cerebro.Poderes.TemComQueAparar);

				// uma rajada RITMADA: e do ritmo que a IA aprende, e nao do `Recarga` do outro
				int aparou = 0, golpes = 0, ergueu = 0;
				double kiGastoNaGuarda = 0;
				for (int volta = 0; volta < 400; volta++)
				{
					bool guardavaAntes = npc.Combate.Bloqueando;
					double kiAntes = npc.Ficha.Ki;
					npc.Ficha.Ki = npc.Ficha.MaxKi;   // isola o custo da guarda do resto do tanque

					if (volta % 12 == 0)
					{
						bate.Combate.Recarga = 0;
						golpes++;
						bool bloqueava = npc.Combate.Bloqueando;
						Atacar(bate, Protocol.Golpe.Leve);
						if (bloqueava && npc.Ficha.Ki < npc.Ficha.MaxKi - 1e-9)
						{
							aparou++;
							kiGastoNaGuarda = npc.Ficha.MaxKi - npc.Ficha.Ki;
						}
					}
					_ = kiAntes;
					Tiques(1, npc, bate);
					if (!guardavaAntes && npc.Combate.Bloqueando) ergueu++;
					// o corpo nao pode cair no meio da medida
					npc.Combate.Corpo.Restaurar();
					npc.Combate.SincronizarVida();
				}

				Checa("ela ERGUE a guarda sozinha durante a troca (o reflexo, a 30 Hz)",
					  ergueu > 0, $"{ergueu} vezes em 400 tiques");
				Checa("...e APARA de verdade (o golpe entra no membro que aparou)",
					  aparou > 0, $"{aparou} de {golpes} golpes");
				Checa("...e NAO apara todos -- 100% de bloqueio e a assinatura de um robo",
					  aparou < golpes, $"{aparou}/{golpes}");
				Checa("...e cada apara COBRA `MaxKi * CustoKiDaGuarda`, igual a do jogador",
					  aparou == 0 || Math.Abs(kiGastoNaGuarda - npc.Ficha.MaxKi * CombatKnobs.CustoKiDaGuarda)
						  < npc.Ficha.MaxKi * CombatKnobs.CustoKiDaGuarda * Folga,
					  $"cobrou {kiGastoNaGuarda:0.###}, devia {npc.Ficha.MaxKi * CombatKnobs.CustoKiDaGuarda:0.###}");
			}

			// =====================================================================
			// 5. O TEMPO DE REACAO: media, variancia e o PISO de 100 ms
			// =====================================================================
			// Isto e o coracao do "parecer gente" que da pra medir. As tres afirmacoes juntas sao
			// o que separa um humano de um robo e de um bebado:
			//   media perto do alvo (ele e atento), variancia > 0 (ele nao e um cronometro),
			//   e um PISO -- nenhuma amostra sobre-humana, nem por sorte.
			{
				var cerebro = new Cerebro { Disciplina = 1.0, TempoDeReacao = 0.25 };
				var rng = new Random(4242);
				var amostras = new List<double>(2000);

				// mede o atraso ENTRE "o alvo entrou no alcance guardavel" e "a guarda subiu",
				// tique a tique, reiniciando o cerebro a cada amostra
				for (int n = 0; n < 1000; n++)
				{
					var c2 = new Cerebro
					{
						Disciplina = 1.0, TempoDeReacao = 0.25,
						Poderes = new Capacidades { TemComQueAparar = true },
					};
					var p = new Percepcao
					{
						TemAlvo = true, Minha = Vec2.Zero, DoAlvo = new Vec2(30, 0),
						VidaFrac = 0.20, KiFrac = 1, Ki = 500, FolegoFrac = 1,
						Atordoado = true, MeuPoder = 1000, PoderDoAlvo = 1000,
					};
					double t = 0;
					for (int i = 0; i < 200; i++)
					{
						t += Protocol.TickSeconds;
						if (c2.Pensar(p, Protocol.TickSeconds, rng).Guardar) { amostras.Add(t); break; }
					}
				}
				_ = cerebro;

				double media = amostras.Count > 0 ? amostras.Average() : 0;
				double min = amostras.Count > 0 ? amostras.Min() : 0;
				double variancia = amostras.Count > 1
					? amostras.Sum(x => (x - media) * (x - media)) / (amostras.Count - 1) : 0;

				Checa("mil amostras de reacao foram colhidas", amostras.Count > 900, $"{amostras.Count}");
				Checa("a VARIANCIA e maior que zero (ele nao e um cronometro)",
					  variancia > 1e-4, $"var={variancia:0.00000}");
				Checa("...e nenhuma reacao em 1000 amostras fica abaixo de 100 ms (nada de clarividencia)",
					  min >= Cerebro.ReacaoMinima - 1e-9, $"minimo {min * 1000:0} ms");
				Checa("...e a media fica na ordem do tempo de reacao pedido (0,25 s)",
					  media is > 0.12 and < 1.20, $"media {media * 1000:0} ms");
			}

			// =====================================================================
			// 6. A CARGA DE KI: recua, PARA, carrega -- e nao carrega andando
			// =====================================================================
			{
				ServerPlayer npc = Forjar("ia: carga", 50_000, new Vec2(0, 2048));
				ServerPlayer alvo = Forjar("alvo: longe", 50_000, new Vec2(900, 2048), comCerebro: false);

				// `SabeReunir` le o `MeditateGivesKiRegen` -- a MESMA porta da tecla C do jogador
				npc.Ficha.MeditateGivesKiRegen = 1;
				npc.Ficha.canPower = 1;
				npc.Cerebro!.Poderes = LerCapacidades(npc);
				npc.Cerebro.Inteligencia = 1.0;   // recarregar e uma decisao de bicho esperto

				Checa("ele SABE reunir energia (pelo `CargaDeKi.SabeReunir`, a porta do C)",
					  npc.Cerebro.Poderes.SabeReunirKi);

				npc.Ficha.Ki = npc.Ficha.MaxKi * 0.05;    // ki critico
				npc.Ficha.stamina = npc.Ficha.maxstamina * 0.05;   // folego critico
				double kiNoFundo = npc.Ficha.Ki;

				Tiques(60, npc, alvo);   // 2 s

				Checa("com ki E folego criticos e o alvo longe, ele CARREGA (o `rechargeState`)",
					  npc.Carregando, $"plano={npc.Cerebro.Atual}");
				Checa("...e enquanto carrega ele NAO ANDA (o mesmo portao que prende o jogador)",
					  !npc.Moving);

				Tiques(120, npc, alvo);
				Checa("...e o Ki SOBE de verdade (pelo `CargaDeKi.Passo`, nao por atribuicao)",
					  npc.Ficha.Ki > kiNoFundo, $"{kiNoFundo:0.#} -> {npc.Ficha.Ki:0.#}");

				// A INTERRUPCAO NOMEADA: o alvo chega perto e a carga tem que morrer.
				alvo.Pos = npc.Pos + new Vec2(24, 0);
				Tiques(90, npc, alvo);
				Checa("o alvo chegando perto INTERROMPE a carga (ela nao e um estado grudado)",
					  !npc.Carregando, $"plano={npc.Cerebro.Atual}");
			}

			// =====================================================================
			// 7. TRANSFORMAR: pelo funil, com as recusas valendo
			// =====================================================================
			{
				// A ESCADA ABERTA PELO MESMO CAMINHO DA FABRICA (`SorteioDeNpc.AbrirFormas`, que
				// sobe degrau a degrau pelo `Proxima`). Escrever `Liberadas.Add` na mao abriria uma
				// forma que o BP talvez nao abrisse, e a bancada mediria um corpo que nao existe.
				var moldeEscada = new Jandirus.Core.Npc.MoldeDeNpc
				{
					Id = "bancada_ia", Racas = ["Saiyan"], Classe = "Saiyan",
					EscadaAutomatica = true, Maestria = 100,
				};

				ServerPlayer npc = Forjar("ia: forma", 5e9, new Vec2(0, 3072));
				ServerPlayer alvo = Forjar("alvo: forte", 5e11, new Vec2(60, 3072), comCerebro: false);
				npc.Ficha.Class = "Saiyan";
				npc.Ficha.MeditateGivesKiRegen = 1;
				npc.Ficha.Ki = npc.Ficha.MaxKi;
				Jandirus.Core.Npc.SorteioDeNpc.AbrirFormas(npc.Forma, moldeEscada, npc.Ficha.BP, Perfil(npc));
				npc.Cerebro!.Poderes = LerCapacidades(npc);
				npc.Cerebro.Inteligencia = 1.0;

				Checa("ha um degrau acima dele (pelo MESMO `Proxima` da tecla C)",
					  npc.Cerebro.Poderes.HaDegrauAcima, $"recusa={npc.Cerebro.Poderes.RecusaDaForma}");
				Checa("...e ele comeca NA BASE", npc.Forma.NaBase, npc.Forma.Atual);

				// o alvo tem 100x o poder dele: e a condicao literal do DM (`>= expressedBP * 1.5`)
				Tiques(200, npc, alvo);
				Checa("contra um alvo muito mais forte, ela SOBE a escada sozinha",
					  !npc.Forma.NaBase, $"plano={npc.Cerebro.Atual} forma={npc.Forma.Atual}");

				// ============================ A RECUSA POR KI, E UM BURACO QUE ELA REVELOU ============================
				// `Avaliar` recusa a forma abaixo de 10% de Ki (`EstadoDeForma.cs:281`), e a IA cobra
				// 25% (o `if(Ki < MaxKi * 0.25) return` do `npc_try_transform`, NPCAI.dm:365).
				//
				// **A BANCADA ACHOU QUE O FUNIL NAO COBRA ESSA RECUSA.** `Transformar` escolhe o degrau
				// pelo `est.Proxima(BP, perfil)`, e `Proxima` avalia com `kiFracao: 1` cravado
				// (`EstadoDeForma.cs:143`) -- ela responde "que degrau este corpo ALCANCA", nao "da pra
				// agora". Como `Transformar` nao reavalia com o Ki de verdade, **o `RecusaForma.SemKi`
				// esta morto pra a tecla C**: um jogador com 2% de Ki sobe de forma na primeira apertada.
				//
				// Isto NAO e desta camada e nao foi mexido aqui: consertar mudaria o comportamento do
				// JOGADOR, e o pedido do dono nao lista o Ki entre as recusas ("BP, maestria, raiva, ki
				// divino"). A checagem afirma o que E VERDADE HOJE e diz onde virar -- e o mesmo padrao
				// que a bancada de NPC ja usa pro `enabled = 0` da arvore de corpo.
				//
				// O QUE ESTA CAMADA GARANTE, E E O QUE IMPORTA AQUI: a IA e mais ESTRITA que o funil,
				// nunca mais frouxa. Ela nao emite o comando com o tanque no fim -- e essa e a
				// checagem de baixo, que e a que reprova se alguem quebrar a IA.
				// ==============================================================================================
				ServerPlayer seco = Forjar("ia: sem folego", 5e9, new Vec2(0, 3200));
				seco.Ficha.Class = "Saiyan";
				Jandirus.Core.Npc.SorteioDeNpc.AbrirFormas(seco.Forma, moldeEscada, seco.Ficha.BP, Perfil(seco));
				seco.Ficha.Ki = seco.Ficha.MaxKi * 0.02;
				seco.Cerebro!.Poderes = LerCapacidades(seco);
				seco.Cerebro.Inteligencia = 1.0;

				Checa("com o tanque no fim, a recusa REAL da forma e `SemKi` -- e ela e REPARAVEL",
					  seco.Cerebro.Poderes.RecusaDaForma == RecusaForma.SemKi
					  && seco.Cerebro.Poderes.FormaSoFaltaKi,
					  $"{seco.Cerebro.Poderes.RecusaDaForma}");

				// A IA COM 2% DE KI NUNCA PEDE A FORMA -- 3000 tiques na situacao que mais a tenta
				// (vida no fim, alvo esmagadoramente mais forte).
				int pediuForma = 0;
				var rngSeco = new Random(7);
				for (int i = 0; i < 3000; i++)
				{
					var p = new Percepcao
					{
						TemAlvo = true, Minha = Vec2.Zero, DoAlvo = new Vec2(40, 0),
						VidaFrac = 0.10, KiFrac = 0.02, Ki = 3, FolegoFrac = 0.5,
						MeuPoder = 1000, PoderDoAlvo = 1_000_000,
					};
					if (seco.Cerebro.Pensar(p, Protocol.TickSeconds, rngSeco).SubirForma) pediuForma++;
				}
				Checa("...e a IA com 2% de Ki NAO pede a forma em 3000 tiques (ela cobra os 25% do DM)",
					  pediuForma == 0, $"{pediuForma} pedidos");

				// E O ACHADO, AFIRMADO COMO ELE E HOJE. Se esta linha reprovar, alguem consertou o
				// `Transformar` -- e ai a afirmacao vira "o funil recusa por Ki", que e a certa.
				Transformar(seco, subir: true);
				Checa("ACHADO: o funil `Transformar` NAO cobra o `SemKi` (o `Proxima` avalia com "
					+ "`kiFracao: 1`) -- se esta linha reprovar, o buraco foi fechado e a checagem tem que virar",
					  !seco.Forma.NaBase, seco.Forma.Atual);
			}

			// =====================================================================
			// 8. O CHEFE QUE TEM A FORMA E NAO A USA -- pelo COMANDO da IA
			// =====================================================================
			// A bancada de NPC ja provou isto apertando C na mao. O que so daqui se ve e que o
			// caminho da IA passa pelo MESMO funil: se o atuador chamasse qualquer outra coisa, o
			// chefe subiria.
			{
				ServerPlayer? chefe = NascerNpc("guardiao_saiyajin", zona, new Vec2(0, 4096), 77);
				if (chefe != null)
				{
					nascidos.Add(chefe);
					chefe.Cerebro = new Cerebro { Inteligencia = 1.0 };
					chefe.Cerebro.Poderes = LerCapacidades(chefe);

					Checa("o guardiao TEM a forma (o `Despertou` dele e verdadeiro)",
						  chefe.Forma.Despertou("ssj1"));
					Checa("...e a capacidade ja diz que ele nao ascende por decisao",
						  !chefe.Cerebro.Poderes.AscendePorDecisao);

					for (int i = 0; i < 20; i++)
						AplicarComando(chefe, new Comando { SubirForma = true }, Protocol.TickSeconds);

					Checa("20 comandos de SUBIR FORMA da IA nao o movem (a guarda esta no funil)",
						  chefe.Forma.NaBase, chefe.Forma.Atual);

					// O CONTROLE. Sem esta metade a checagem acima passaria ate com o `Transformar`
					// apagado -- e o mesmo par que a bancada de NPC ja usa.
					Jandirus.Core.Npc.PapelDeNpc guardado = chefe.Papel!;
					chefe.Papel = null;
					AplicarComando(chefe, new Comando { SubirForma = true }, Protocol.TickSeconds);
					Checa("...e o MESMO comando o move assim que o papel sai (era a guarda, e nao outra recusa)",
						  !chefe.Forma.NaBase, chefe.Forma.Atual);
					Transformar(chefe, subir: false);
					chefe.Papel = guardado;
				}
				else Checa("o guardiao nasceu", false, "molde 'guardiao_saiyajin' nao carregou");
			}

			// =====================================================================
			// 9. A CORRIDA COBRA -- o atalho que passaria em toda bancada funcional
			// =====================================================================
			// Escrever `npc.Correndo = true` daria 60% de velocidade DE GRACA e o boneco na tela
			// seria identico. O que denuncia e o tanque.
			{
				ServerPlayer npc = Forjar("ia: corrida", 50_000, new Vec2(0, 5120), comCerebro: false);
				double kiAntes = npc.Ficha.Ki;
				Vec2 posAntes = npc.Pos;
				const int n = 30;   // 1 s
				for (int i = 0; i < n; i++)
					PassoDaIa(npc, new Comando { Rumo = new Vec2(1, 0), Correndo = true }, Protocol.TickSeconds);

				double gasto = kiAntes - npc.Ficha.Ki;
				double devia = npc.Ficha.MaxKi * CustoCorridaPorSegundo * n * Protocol.TickSeconds;

				Checa("correr COBRA Ki na IA, com a mesma taxa do `PodeCorrer` do jogador",
					  Math.Abs(gasto - devia) < devia * Folga + 1e-6,
					  $"gastou {gasto:0.####}, devia {devia:0.####}");
				Checa("...e o corpo andou de fato na velocidade de corrida",
					  (npc.Pos - posAntes).Length > MoveRules.SpeedPx(npc.SpeedStat, false) * 1.2,
					  $"{(npc.Pos - posAntes).Length:0.#} px em 1 s");
				Checa("...e o `dashing` ficou ligado (ele entra na conta de dano dos dois lados)",
					  npc.Ficha.dashing);

				// SEM KI, NAO CORRE -- exatamente como o jogador.
				npc.Ficha.Ki = 0;
				PassoDaIa(npc, new Comando { Rumo = new Vec2(1, 0), Correndo = true }, Protocol.TickSeconds);
				Checa("...e com o tanque vazio ela simplesmente NAO corre", !npc.Correndo);
			}

			// =====================================================================
			// 10. A FERA NAO REGREDIU: `Disciplina = 0` nunca ergue a guarda
			// =====================================================================
			{
				var fera = new Cerebro
				{
					VidaCautelosa = 0, ChanceDePesado = 0.85, IntervaloDeDecisao = 0.5,
					Disciplina = 0, Inteligencia = 0,
					Poderes = new Capacidades { TemComQueAparar = true, SabeReunirKi = true, PodeVoar = true },
				};
				var rng = new Random(999);
				int guardou = 0, carregou = 0, voou = 0, recuou = 0;
				for (int i = 0; i < 5000; i++)
				{
					var p = new Percepcao
					{
						TemAlvo = true, Minha = Vec2.Zero,
						DoAlvo = new Vec2((float)(rng.NextDouble() * 200 - 100), 0),
						AltitudeDoAlvo = (float)(rng.NextDouble() * Voo.AlturaMaxima),
						AlvoVoando = rng.Next(2) == 0,
						VidaFrac = rng.NextDouble() * 0.4,        // sempre machucada
						KiFrac = rng.NextDouble() * 0.1,          // sempre sem ki
						Ki = rng.NextDouble() * 10,
						FolegoFrac = rng.NextDouble() * 0.1,      // sempre sem folego
						Atordoado = rng.Next(2) == 0,
						MeuPoder = 1000, PoderDoAlvo = 100000,    // sempre em desvantagem
					};
					Comando c = fera.Pensar(p, Protocol.TickSeconds, rng);
					if (c.Guardar) guardou++;
					if (c.Carregar) carregou++;
					if (c.AlternarVoo) voou++;
					if (fera.Atual == Plano.Recuar) recuou++;
				}
				Checa("a fera NUNCA ergue a guarda, em 5000 tiques da pior situacao possivel",
					  guardou == 0, $"{guardou} tiques guardando");
				Checa("...nem recarrega (`Inteligencia = 0` tira a receita, como o `prob(ai_intelligence)`)",
					  carregou == 0, $"{carregou}");
				Checa("...nem decide usar altura", voou == 0, $"{voou}");
				Checa("...nem recua (`VidaCautelosa = 0`, como sempre foi)", recuou == 0, $"{recuou}");

				// ============================ E ELA CONTINUA VAGANDO SEM PRESA ============================
				// A primeira versao desta camada QUEBROU isto: com planos, `!TemAlvo` caia em `Nada` e a
				// fera parava de andar. Quem pegou foi a `--formasteste` ("o corpo possuido se move
				// sozinho"), e nao esta bancada -- porque todas as percepcoes daqui tinham alvo.
				// Esta checagem existe pra que a proxima quebra caia AQUI, do lado da causa.
				// =====================================================================================
				int andou = 0, socouOVento = 0;
				for (int i = 0; i < 300; i++)
				{
					// SEM ALVO, e com um ponto de destino -- e o que `RumoDaFera` produz
					var p = new Percepcao
					{
						TemAlvo = false, Minha = Vec2.Zero, DoAlvo = new Vec2(200, 0),
						VidaFrac = 1, KiFrac = 1, Ki = 500, FolegoFrac = 1,
					};
					Comando c = fera.Pensar(p, Protocol.TickSeconds, rng);
					if (c.Rumo.LengthSquared > 1e-6f) andou++;
					if (c.Leve || c.Pesado) socouOVento++;
				}
				Checa("...mas SEM PRESA ela continua vagando (o ponto do `RumoDaFera`)",
					  andou > 250, $"{andou} de 300 tiques andando");
				Checa("...e nao soca o ponto pra onde esta indo (ponto nao e ninguem)",
					  socouOVento == 0, $"{socouOVento}");
			}

			// =====================================================================
			// 11. HISTERESE: o plano nao troca a cada quadro
			// =====================================================================
			{
				var c = new Cerebro
				{
					Inteligencia = 0.9, Disciplina = 0.5,
					Poderes = new Capacidades { TemComQueAparar = true, SabeReunirKi = true },
				};
				var rng = new Random(31337);
				Plano antes = c.Atual;
				int trocas = 0;
				// uma situacao AMBIGUA de proposito: vida e ki no fio da navalha, onde uma IA sem
				// compromisso ficaria vibrando entre pressionar e recuar
				for (int i = 0; i < 3000; i++)
				{
					var p = new Percepcao
					{
						TemAlvo = true, Minha = Vec2.Zero, DoAlvo = new Vec2(60, 0),
						VidaFrac = 0.44 + (rng.NextDouble() - 0.5) * 0.03,
						KiFrac = 0.11 + (rng.NextDouble() - 0.5) * 0.03,
						Ki = 100, FolegoFrac = 0.13 + (rng.NextDouble() - 0.5) * 0.03,
						MeuPoder = 1000, PoderDoAlvo = 1000,
					};
					c.Pensar(p, Protocol.TickSeconds, rng);
					if (c.Atual != antes) { trocas++; antes = c.Atual; }
				}
				double segundos = 3000 * Protocol.TickSeconds;
				Checa("num empate proposital, o plano nao troca mais que uma vez por "
					+ $"{Cerebro.TempoMinimoNoPlano:0.0} s (o compromisso)",
					  trocas <= segundos / Cerebro.TempoMinimoNoPlano + 1,
					  $"{trocas} trocas em {segundos:0.#} s");
				Checa("...e ele tambem nao CONGELA (compromisso nao e paralisia)", trocas > 0);
			}

			// =====================================================================
			// 12. O CUSTO POR TIQUE, e o lixo por tique
			// =====================================================================
			{
				// ============================ LONGE UNS DOS OUTROS, E ISSO E A MEDIDA ============================
				// Na primeira rodada os 20 nasceram a 48 px de distancia -- ou seja dentro do alcance
				// de soco -- e ficaram brigando durante a medicao. O numero que saiu (1225 B por
				// tique "barato") era do COMBATE, nao da IA: `EscolherGuarda` monta uma lista de
				// candidatos por golpe, `Estados()` monta um dicionario. Sao caminhos de producao,
				// compartilhados com o jogador, e nada disso e desta camada.
				//
				// A 4000 px eles percebem, decidem, andam e nunca alcancam -- que e exatamente o
				// pedaco que este trabalho e dono e o unico que faz sentido cobrar zero.
				// ==========================================================================================
				// ============================ E A ZONA TEM QUE ESTAR LIMPA ============================
				// Os corpos das secoes anteriores continuam no mundo COM CEREBRO, brigando entre si e
				// com relogios de 1 Hz espalhados pelo segundo. Eles cairam dentro da janela "barata"
				// e produziram 1225 B por tique -- um numero perfeitamente estavel, medido tres vezes,
				// que eu quase atribui a percepcao. **A medida errada mentiu com casas decimais.**
				//
				// Tirar o cerebro dos outros nao os remove do mundo: eles continuam no `_players` e
				// continuam sendo VARRIDOS pelo laco, que e o custo real de um servidor cheio.
				// ==================================================================================
				foreach (ServerPlayer velho in _players.Values) velho.Cerebro = null;

				var corpos = new List<ServerPlayer>();
				for (int i = 0; i < 20; i++)
				{
					ServerPlayer c = Forjar($"ia: perf{i}", 50_000, new Vec2(i * 4000, 6144));
					EnsinarAVoar(c);
					c.Cerebro!.Poderes = LerCapacidades(c);
					corpos.Add(c);
				}

				// ============================ SAO DOIS TIQUES DIFERENTES, E MEDIR JUNTO ESCONDE OS DOIS ============================
				// 29 de cada 30 tiques sao BARATOS: percebe, decide, atua. O trigesimo carrega a
				// leitura de capacidades (1 Hz por corpo), que varre o catalogo de formas inteiro --
				// e la dentro cada `Avaliar` monta um `HashSet` (`Catalogo.LinhasAbertas`).
				//
				// Medir a media dos 30 da um numero que nao descreve nenhum dos dois e que nao pega
				// regressao: um vazamento no tique barato desaparece na media, e uma leitura duas
				// vezes mais cara tambem. Entao sao duas medidas separadas, com dois limites.
				// ==============================================================================================================
				double Medir(int quantos, bool comLeitura, out long bytes)
				{
					for (int i = 0; i < corpos.Count; i++)
						if (i >= quantos && corpos[i].Cerebro != null) corpos[i].Cerebro = null;

					TickDosCorposSemDono(Protocol.TickSeconds);   // aquece (JIT)

					// ============================ SINCRONIZAR OS RELOGIOS DE 1 Hz ============================
					// Um tique NAO alinha os relogios: cada corpo tem o seu, e depois das medicoes
					// anteriores eles estao espalhados pelo segundo -- entao ~16 leituras caiam
					// dentro da janela "barata" e o numero media 1225 B, sempre igual, o que quase me
					// convenceu de que era outra coisa.
					//
					// Um `dt` de 1 s zera e rearma TODOS de uma vez (e a mesma funcao, sem atalho),
					// e a partir dai ha 30 tiques garantidamente sem leitura.
					// ==================================================================================
					if (!comLeitura)
						for (int i = 0; i < quantos; i++) corpos[i].Cerebro?.PrecisaLerCapacidades(1.0);

					int voltas = comLeitura ? 300 : 25;

					long b0 = GC.GetAllocatedBytesForCurrentThread();
					ulong t0 = Time.GetTicksUsec();
					for (int i = 0; i < voltas; i++) TickDosCorposSemDono(Protocol.TickSeconds);
					ulong t1 = Time.GetTicksUsec();
					bytes = (GC.GetAllocatedBytesForCurrentThread() - b0) / voltas;
					return (t1 - t0) / (double)voltas;
				}

				// ============================ O CONTROLE DO INSTRUMENTO ============================
				// Antes de acusar a IA de alocar, mede-se o NADA: 25 voltas de um laco vazio, com as
				// mesmas chamadas de relogio e de contador em volta. Se este numero nao for zero, o
				// que a medicao de cima mostra e o instrumento, e nao o sistema.
				// ==============================================================================
				long c0 = GC.GetAllocatedBytesForCurrentThread();
				long soma = 0;
				for (int i = 0; i < 25; i++) soma += i;
				long lixoDoNada = (GC.GetAllocatedBytesForCurrentThread() - c0) / 25;
				_ = soma;

				double us20 = Medir(20, true, out long b20);
				double us5 = Medir(5, true, out long b5);
				double us1 = Medir(1, true, out long b1);
				double barato20 = Medir(20, false, out long lixo20);
				// O PISO: o laco VARRENDO tudo e nao dirigindo ninguem. Sem esta linha nao da pra
				// saber se um numero de lixo e da IA ou do laco.
				double vazio = Medir(0, false, out long lixoVazio);

				GD.Print("  ---- custo do `TickDosCorposSemDono` (por tique) ----");
				GD.Print($"       1 corpo  : {us1:0.0} us, {b1} B");
				GD.Print($"       5 corpos : {us5:0.0} us, {b5} B");
				GD.Print($"      20 corpos : {us20:0.0} us, {b20} B   <- media com a leitura de 1 Hz diluida");
				GD.Print($"      20 corpos, tique BARATO (sem leitura): {barato20:0.0} us, {lixo20} B");
				GD.Print($"       0 corpos (so o laco varrendo `_players`): {vazio:0.0} us, {lixoVazio} B");
				GD.Print($"       CONTROLE (laco vazio, sem tique nenhum): {lixoDoNada} B");
				GD.Print($"      (o tique inteiro tem {Protocol.TickSeconds * 1e6:0} us de orcamento)");

				Checa("20 corpos dirigidos cabem folgados num tique de 33 ms",
					  us20 < Protocol.TickSeconds * 1e6 * 0.25,
					  $"{us20:0.0} us de {Protocol.TickSeconds * 1e6:0} us");

				// ============================ O LIXO E O QUE APARECE DEPOIS ============================
				// Tempo de CPU num servidor vazio nao denuncia nada; alocacao por tique sim -- ela
				// vira pausa de coletor numa sessao de horas, e ninguem liga a pausa a IA.
				//
				// NO TIQUE BARATO O ALVO E ZERO, e ele e alcancavel: `Percepcao` e `Comando` sao
				// `struct`, o buffer dos dirigidos e reusado, e o relato so monta string quando
				// alguem liga o `Explicando`.
				// ==================================================================================
				Checa("o tique BARATO da IA nao aloca nada -- percepcao, decisao, comando e passo de "
					+ "20 corpos, zero bytes (struct, buffer reusado, relato desligado)",
					  lixo20 < 64, $"{lixo20} B por tique com 20 corpos");

				// E O CARO E AFIRMADO COMO ELE E, com a causa nomeada -- a checagem existe pra pegar
				// REGRESSAO, e nao pra prometer um zero que o catalogo compartilhado nao permite.
				Checa("a leitura de capacidades (1 Hz) fica dentro do orcamento de lixo conhecido "
					+ "-- a fonte e o `HashSet` que `Catalogo.LinhasAbertas` monta por `Avaliar`, "
					+ "dentro do `Proxima`; se esta linha reprovar, alguem pos um laco novo no caminho",
					  b20 < 8_192, $"{b20} B por tique (media) com 20 corpos");
			}

			// =====================================================================
			// 13. O GANCHO DO ATAQUE DE LONGE: hoje INERTE, e provado dos dois lados
			// =====================================================================
			// ============================ O QUE ESTA SECAO PRECISA PROVAR, E POR QUE SAO DUAS COISAS ============================
			// Um gancho tem dois jeitos de estar errado, e eles sao opostos:
			//
			//   * ELE DISPARA. Alguem registra uma tecnica sem querer, ou a viabilidade tem um buraco,
			//     e o NPC para de socar pra conjurar um efeito que nao existe. Na tela isso e "o bicho
			//     ficou parado olhando pro nada" -- e ninguem liga isso a esta camada.
			//   * ELE E ENFEITE. Compila, ninguem chama, e no dia do beam se descobre que a receita
			//     nunca rodou uma vez sequer. Este repo ja pagou por isso (35 atlas escritos e nunca
			//     importados; a API de sigilo de BP 100% orfa).
			//
			// Uma bancada que so afirmasse "hoje nao dispara" garantiria contra o primeiro e seria
			// CUMPLICE do segundo. Entao sao dois blocos: (A) com o arsenal DE VERDADE, e (B) com um
			// `Tiro` sintetico posto na mao -- sem tocar na tabela global --, exercitando as cinco
			// perguntas do dono uma a uma.
			//
			// O BLOCO (A) MUDOU DE SENTIDO quando os ataques de ki chegaram. Ele afirmava que a tabela
			// estava vazia e que ninguem tinha arsenal; hoje ha tres tecnicas que VIAJAM (raio, bola,
			// teleguiado) e o que ele afirma e a outra metade da mesma pergunta: quem SABE tem, quem
			// NAO SABE nao tem, e a linha de visao continua sendo paga so por quem tem.
			// ==============================================================================================================
			{
				// ---------- (A) COM O JOGO COMO ELE E HOJE ----------
				Checa("as tecnicas que VIAJAM estao na tabela de ataque de longe (uma por tipo)",
					  TecnicasDeLonge.Alguma && TecnicasDeLonge.Quantas == 3,
					  $"{TecnicasDeLonge.Quantas} registradas");
				Checa("...e as instantaneas continuam FORA (o `Light_Buster` e golpe de aproximacao)",
					  TecnicasDeLonge.Get("Light_Buster") == null && TecnicasDeLonge.Get("Solar_Flare") == null);

				// QUEM NAO SABE NENHUMA DELAS CONTINUA SEM ARSENAL -- a metade que protege o resto do
				// jogo: o ramo de longe inteiro tem que morrer numa comparacao pra quem nao atira.
				ServerPlayer pobre = Forjar("ia: nao sabe nada", 50_000, new Vec2(0, 8320));
				pobre.Cerebro!.Poderes = LerCapacidades(pobre);
				Checa("quem nao comprou nenhuma delas sai com o arsenal de longe VAZIO",
					  !pobre.Cerebro.Poderes.DeLonge.TemAlguma,
					  $"{pobre.Cerebro.Poderes.DeLonge.Quantas} opcoes");

				ServerPlayer sabido = Forjar("ia: sabe tudo", 50_000, new Vec2(0, 8192));

				// O CORPO QUE SABE TUDO: aprender o catalogo inteiro e o teste mais duro contra os dois
				// erros opostos -- "nao acha o que existe" e "acha o que nao existe".
				int aprendidas = 0;
				if (_skills != null)
					foreach (Skill s in _skills.Todas)
					{ sabido.Livro.Dar(s.Path); aprendidas++; }

				sabido.Cerebro!.Poderes = LerCapacidades(sabido);

				// ============================ COMPRAR A SKILL NAO E O UNICO JEITO ============================
				// Das tres tecnicas que voam, so o `Ki_Wave` e concedido por uma SKILL (`skills.json`).
				// As outras duas vem por NIVEL -- `Basic_Blast` no degrau 35 de `Ki_Unlocked`,
				// `Guided_Ball` no 30 de `Basic_Ki_Control` (`niveis.json`) --, e era exatamente isso que
				// `NiveisDeSkill.VerbosAtivos()` respondia sem que ninguem perguntasse. Foi esta
				// afirmacao que achou a funcao orfa: um corpo com o catalogo inteiro saia com UMA opcao.
				// ========================================================================================
				Checa($"o corpo que aprendeu o catalogo INTEIRO ({aprendidas} skills) sai com o RAIO -- "
					+ "o unico que uma skill destrava",
					  sabido.Cerebro.Poderes.DeLonge.Quantas == 1,
					  $"{sabido.Cerebro.Poderes.DeLonge.Quantas} opcoes");

				// E AGORA OS DEGRAUS, pelo caminho de producao (o mesmo `DoSave` do login).
				var comNiveis = new NivelSave();
				comNiveis.Skills["/datum/skill/mind/Ki_Unlocked"] = [35, 0];
				comNiveis.Skills["/datum/skill/mind/Basic_Ki_Control"] = [30, 0];
				sabido.Niveis.DoSave(comNiveis);

				sabido.Cerebro.Poderes = LerCapacidades(sabido);
				Checa("...e subindo os degraus que concedem as outras duas, o arsenal sai com as TRES",
					  sabido.Cerebro.Poderes.DeLonge.Quantas == 3,
					  $"{sabido.Cerebro.Poderes.DeLonge.Quantas} opcoes");
				Checa("...e o jogo passa a ACEITAR os verbs vindos de nivel (`SabeTecnica`, o mesmo gate)",
					  SabeTecnica(sabido, "Basic_Blast") && SabeTecnica(sabido, "Guided_Ball"));

				// A LINHA DE VISAO NAO E TRACADA. O argumento `quemAtira` vem do arsenal, e com ele
				// falso o `PathBlocked` -- a varredura de segmento -- nem chega a ser chamado. E o que
				// mantem o gancho fora do caminho de 30 Hz enquanto ele nao servir pra nada.
				ServerPlayer vizinho = Forjar("alvo: vizinho", 50_000, new Vec2(64, 8192), comCerebro: false);
				Percepcao semTiro = LerPercepcao(pobre, vizinho, vizinho.Pos, quemAtira: false);
				Percepcao comTiro = LerPercepcao(sabido, vizinho, vizinho.Pos, quemAtira: true);
				Checa("sem arsenal, a percepcao nem PERGUNTA pela linha de visao (falso = nao sei = nao atira)",
					  !semTiro.LinhaLivre);
				Checa("...e quando alguem tiver o que atirar, a pergunta e feita e responde de verdade "
					+ "(dois corpos colados, sem parede no meio)", comTiro.LinhaLivre);
				Checa("...e a mira ja vem na percepcao (o id do alvo, o mesmo numero do `C2S.Alvo`)",
					  comTiro.IdDoAlvo == vizinho.Id, $"{comTiro.IdDoAlvo} != {vizinho.Id}");

				// 10 MIL PERCEPCOES SORTEADAS. Duas passadas com o MESMO laco, e a diferenca entre elas
				// e so QUEM tem arsenal -- que e exatamente o que a poda promete decidir.
				{
					var cerebro = new Cerebro { Inteligencia = 1.0, Disciplina = 1.0 };
					cerebro.Poderes = LerCapacidades(pobre);
					var rng = new Random(20260812);
					int pediu = 0, mirou = 0, planejou = 0;
					for (int i = 0; i < 10_000; i++)
					{
						var p = new Percepcao
						{
							TemAlvo = true, IdDoAlvo = 4242, Minha = Vec2.Zero,
							DoAlvo = new Vec2((float)(rng.NextDouble() * 1200 - 600), 0),
							AltitudeDoAlvo = (float)(rng.NextDouble() * Voo.AlturaMaxima),
							AlvoVoando = rng.Next(2) == 0, AlvoSeMovendo = rng.Next(2) == 0,
							LinhaLivre = rng.Next(2) == 0,
							VidaFrac = rng.NextDouble(), KiFrac = rng.NextDouble(),
							Ki = rng.NextDouble() * 5000, FolegoFrac = rng.NextDouble(),
							MeuPoder = 1000, PoderDoAlvo = rng.NextDouble() * 5000,
						};
						Comando c = cerebro.Pensar(p, Protocol.TickSeconds, rng);
						if (c.Habilidade != null) pediu++;
						if (c.Marcar != 0) mirou++;
						if (cerebro.Atual == Plano.Atirar) planejou++;
					}
					Checa("sem arsenal, 10.000 percepcoes nao produzem UM pedido de tecnica",
						  pediu == 0 && planejou == 0, $"{pediu} pedidos, {planejou} tiques no plano de atirar");
					Checa("...e ela tambem nao MIRA em ninguem (a mira nasceu pro tiro, e nao muda o soco)",
						  mirou == 0, $"{mirou}");
				}

				// A MESMA VARREDURA, AGORA COM QUEM TEM OS TRES ATAQUES DE KI NA MAO.
				//
				// Esta e a afirmacao que fecha tres camadas de gancho inerte: o plano de atirar deixou
				// de ser inalcancavel, e o pedido que sai por ele e o id de um verb DE VERDADE -- o
				// mesmo que o `C2S.Habilidade` do jogador carrega. Sem ela, "o gancho acordou" seria
				// uma frase de relatorio em vez de uma medicao.
				{
					var cerebro = new Cerebro { Inteligencia = 1.0, Disciplina = 1.0 };
					cerebro.Poderes = LerCapacidades(sabido);
					var rng = new Random(20260812);
					int pediu = 0, planejou = 0;
					var quais = new HashSet<string>();
					for (int i = 0; i < 10_000; i++)
					{
						var p = new Percepcao
						{
							TemAlvo = true, IdDoAlvo = 4242, Minha = Vec2.Zero,
							DoAlvo = new Vec2((float)(rng.NextDouble() * 1200 - 600), 0),
							AltitudeDoAlvo = (float)(rng.NextDouble() * Voo.AlturaMaxima),
							AlvoVoando = rng.Next(2) == 0, AlvoSeMovendo = rng.Next(2) == 0,
							LinhaLivre = rng.Next(2) == 0,
							VidaFrac = rng.NextDouble(), KiFrac = rng.NextDouble(),
							Ki = rng.NextDouble() * 5000, FolegoFrac = rng.NextDouble(),
							MeuPoder = 1000, PoderDoAlvo = rng.NextDouble() * 5000,
						};
						Comando c = cerebro.Pensar(p, Protocol.TickSeconds, rng);
						if (c.Habilidade is { Length: > 0 } id) { pediu++; quais.Add(id); }
						if (cerebro.Atual == Plano.Atirar) planejou++;
					}
					Checa("COM arsenal, o plano de atirar deixa de ser inalcancavel",
						  planejou > 0, $"{planejou} tiques no plano de atirar");
					Checa("...e o que sai sao pedidos de tecnicas DE VERDADE, pelo canal do jogador",
						  pediu > 0 && quais.All(q => TecnicasDeLonge.Get(q) != null),
						  $"{pediu} pedidos: {string.Join(", ", quais)}");
				}

				// ---------- (B) O GANCHO EXERCITADO, COM UM TIRO SINTETICO ----------
				// ============================ POR QUE SINTETICO, E NAO UMA LINHA NA TABELA ============================
				// Registrar uma tecnica de teste em `TecnicasDeLonge` valeria pro PROCESSO INTEIRO --
				// esta bancada roda num servidor VIVO, no primeiro login --, e a partir dali todo NPC
				// da sessao tentaria atirar um efeito que nao existe. O `Tiro` e um struct: montar um
				// na mao e po-lo nas `Capacidades` daquele cerebro exercita exatamente o mesmo caminho
				// (viabilidade, escolha, receita, pulso) sem tocar em uma virgula de estado global.
				// ================================================================================================
				var golpe = new Tiro
				{
					Id = "bancada_de_longe",
					AlcanceMin = 4 * ZoneCollision.TileSize,
					AlcanceMax = 12 * ZoneCollision.TileSize,
					TempoDeConjuracao = 1.0,
					CustoDeKi = 100,
					PrecisaDeLinhaLivre = true,
					Precisao = 0.8,
				};

				Cerebro ComTiro(Tiro t, double inteligencia = 1.0) => new()
				{
					Inteligencia = inteligencia, Disciplina = 0,
					Poderes = new Capacidades { DeLonge = new Arsenal([t]) },
				};

				/// uma percepcao de tiro: alvo a `tiles` de distancia, parado, com linha livre
				static Percepcao Cena(float tiles, bool linha = true, bool movendo = false,
									  double ki = 5000, bool voando = false) => new()
				{
					TemAlvo = true, IdDoAlvo = 777, Minha = Vec2.Zero,
					DoAlvo = new Vec2(tiles * ZoneCollision.TileSize, 0),
					LinhaLivre = linha, AlvoSeMovendo = movendo,
					VidaFrac = 1, KiFrac = 1, Ki = ki, FolegoFrac = 1,
					EstouVoando = voando,
					MeuPoder = 1000, PoderDoAlvo = 1000,
				};

				/// roda ate o pulso sair, devolvendo em quantos SEGUNDOS ele saiu (-1 = nao saiu)
				static double AteAtirar(Cerebro c, Percepcao p, int tiques = 300)
				{
					var rng = new Random(2026);
					double t = 0;
					for (int i = 0; i < tiques; i++)
					{
						t += Protocol.TickSeconds;
						if (c.Pensar(p, Protocol.TickSeconds, rng).Habilidade != null) return t;
					}
					return -1;
				}

				// 1. ALCANCE + 5. TEMPO DE CONJURACAO: na janela, ele para, conjura e SOLTA.
				{
					Cerebro c = ComTiro(golpe);
					double quando = AteAtirar(c, Cena(8));
					Checa("com um ataque de longe no arsenal, ela ESCOLHE atirar e o golpe sai",
						  quando > 0, $"nao saiu em {300 * Protocol.TickSeconds:0.#} s");
					Checa("...e ele NAO sai antes do tempo de conjuracao (a janela pra interromper)",
						  quando >= golpe.TempoDeConjuracao, $"saiu em {quando:0.00} s de {golpe.TempoDeConjuracao:0.00}");

					// ============================ O TETO PRECISA DO COMPROMISSO DENTRO ============================
					// Este limite ja reprovou uma vez marcado como `conjuracao + 1 s`, com 2,23 s, e a
					// causa nao era o tiro: um cerebro RECEM-NASCIDO esta no plano `Nada`, e o
					// compromisso (`TempoMinimoNoPlano`, 1,2 s) segura o primeiro plano tanto quanto
					// segura os outros -- nada na lista de interrupcoes cobre "sair do nada".
					//
					// E comportamento de producao e vale pra TODAS as receitas (a fera tambem demora
					// isso pra comecar a vagar); nao e do gancho, e nao vou consertar comportamento
					// antigo dentro de uma camada de terreno. O que o teto tem que dizer e "a decisao
					// nao COME o gesto", entao ele e a soma honesta das tres esperas.
					// ==========================================================================================
					double teto = Cerebro.TempoMinimoNoPlano + golpe.TempoDeConjuracao + 2 * c.IntervaloDeDecisao;
					Checa("...e nao demora mais que o compromisso + a conjuracao (a decisao nao come o gesto)",
						  quando < teto, $"{quando:0.00} s, teto {teto:0.00} s");
					Checa("...e o pulso carrega o id do verb, que e o que o `C2S.Habilidade` manda",
						  c.Atual == Plano.Atirar);
				}

				// 1b. LONGE DEMAIS: fora do alcance maximo o plano nem nasce.
				Checa("alem do alcance maximo ela nao tenta (e volta a pressionar)",
					  AteAtirar(ComTiro(golpe), Cena(30)) < 0);

				// 1c. COLADO: ela RECUA antes de conjurar -- o "kitar" nasce da janela, sem codigo de kite.
				{
					Cerebro c = ComTiro(golpe);
					var rng = new Random(5);
					Percepcao perto = Cena(1);
					int recuou = 0, atirou = 0;
					for (int i = 0; i < 120; i++)
					{
						Comando cm = c.Pensar(perto, Protocol.TickSeconds, rng);
						if (cm.Rumo.X < -0.1f) recuou++;
						if (cm.Habilidade != null) atirou++;
					}
					Checa("colada no alvo ela ABRE DISTANCIA em vez de conjurar (a janela minima)",
						  recuou > 50 && atirou == 0, $"{recuou} tiques recuando, {atirou} tiros");
				}

				// 1d. E O BURRO NAO RECUA PRA ATIRAR -- erro CARACTERISTICO, e nao aleatorio.
				{
					Cerebro burro = ComTiro(golpe, inteligencia: 0.2);
					var rng = new Random(6);
					int recuou = 0;
					Percepcao perto = Cena(1);
					for (int i = 0; i < 120; i++)
						if (burro.Pensar(perto, Protocol.TickSeconds, rng).Rumo.X < -0.1f) recuou++;
					Checa("...mas o bicho BURRO nao lembra que tem raio quando voce cola nele",
						  recuou == 0, $"{recuou} tiques recuando");
				}

				// 2. LINHA DE VISAO: com parede no meio, nunca.
				Checa("com parede no caminho, um golpe que precisa de linha livre NAO sai",
					  AteAtirar(ComTiro(golpe), Cena(8, linha: false)) < 0);
				Checa("...e o mesmo golpe sem essa exigencia sai (a parede so vale pra quem precisa dela)",
					  AteAtirar(ComTiro(golpe with { PrecisaDeLinhaLivre = false }), Cena(8, linha: false)) > 0);

				// 3. CUSTO DE KI: nao atira o que nao paga, e nao se derruba do ceu pra atirar.
				Checa("sem Ki pro custo, ela nao conjura (o custo e PISO da decisao)",
					  AteAtirar(ComTiro(golpe), Cena(8, ki: 50)) < 0);
				Checa("...e no AR ela guarda a reserva que a impede de cair do ceu depois do golpe",
					  AteAtirar(ComTiro(golpe), Cena(8, ki: 105, voando: true)) < 0,
					  "atirou com o tanque no fio, voando");
				Checa("...e no CHAO o mesmo tanque serve (a reserva e sobre cair, e no chao nao se cai)",
					  AteAtirar(ComTiro(golpe), Cena(8, ki: 105)) > 0);

				// 4. RISCO DE ERRAR: o esperto espera o alvo PARAR.
				Checa("contra alvo em movimento no limite do alcance, a esperta NAO larga o golpe",
					  AteAtirar(ComTiro(golpe), Cena(12, movendo: true)) < 0);
				Checa("...e larga assim que ele para (mesma distancia, so o movimento mudou)",
					  AteAtirar(ComTiro(golpe), Cena(12)) > 0);
				Checa("...e a burra larga do mesmo jeito, com o alvo correndo (a tolerancia e a inteligencia)",
					  AteAtirar(ComTiro(golpe, inteligencia: 0.35), Cena(12, movendo: true)) > 0);

				// A CONTA DO RISCO E PURA, e da pra afirmar sem cerebro nenhum
				Checa("o risco cresce com a distancia dentro da janela",
					  Arsenal.RiscoDeErrar(golpe, golpe.AlcanceMax, false)
					  > Arsenal.RiscoDeErrar(golpe, golpe.AlcanceMin, false));
				Checa("...e alvo em movimento erra mais que alvo parado, na mesma distancia",
					  Arsenal.RiscoDeErrar(golpe, golpe.AlcanceMax, true)
					  > Arsenal.RiscoDeErrar(golpe, golpe.AlcanceMax, false));

				// A INTERRUPCAO NOMEADA: apanhar no meio do gesto larga o gesto.
				{
					// ============================ ESTE TESTE JA MENTIU UMA VEZ ============================
					// Escrito com 8 tiques de "conjurando" antes do dano, ele reprovou com "1 tiro" -- e
					// a leitura obvia (o aborto nao funciona) estava ERRADA. Em 8 tiques o cerebro ainda
					// estava no `Nada` por causa do compromisso: nao havia conjuracao pra abortar, e o
					// que a bancada mediu foi um tiro que comecou DEPOIS do dano, legitimamente.
					//
					// Um teste que prepara o estado por CONTAGEM DE TIQUES esta chutando; agora ele
					// AFIRMA o estado antes de mexer nele. E a mesma licao do `expressedBP` que nascia
					// zero no corpo forjado: o setup errado reprova o codigo certo.
					// ================================================================================
					Cerebro c = ComTiro(golpe);
					var rng = new Random(9);
					Percepcao boa = Cena(8);
					int ate = 0;
					while (ate < 200 && !(c.Atual == Plano.Atirar && ate > 0))
					{ c.Pensar(boa, Protocol.TickSeconds, rng); ate++; }
					for (int i = 0; i < 5; i++) c.Pensar(boa, Protocol.TickSeconds, rng);   // conjurando de verdade

					Checa("(preparo) ela esta MESMO conjurando antes de levar o dano",
						  c.Atual == Plano.Atirar, $"plano={c.Atual} depois de {ate} tiques");

					var apanhou = boa with { VidaFrac = 0.6 };   // -40% de vida no meio da conjuracao
					int atirou = 0;
					bool largou = false;
					for (int i = 0; i < 40; i++)
					{
						if (c.Pensar(apanhou, Protocol.TickSeconds, rng).Habilidade != null) atirou++;
						if (c.Atual != Plano.Atirar) largou = true;
					}
					Checa("apanhar no meio da conjuracao ABORTA o golpe (a mesma regra da recarga de Ki)",
						  atirou == 0 && largou, $"{atirou} tiros depois de levar dano, largou={largou}");
				}

				// ---------- (C) O ATUADOR: o pulso vai pelo FUNIL do jogador ----------
				// ============================ E AQUI QUE UM ATALHO APARECERIA ============================
				// O cerebro emitir o id nao prova nada: o atalho seria o atuador chamar o EFEITO
				// direto (`Kamehameha(npc)`), pulando recarga, custo e o "voce nao sabe isso". O que
				// prova o funil e a RECUSA -- e a frase que o jogo devolve e a mesma que um jogador
				// leria. A escuta de avisos e o unico jeito de um teste ver isso, porque `Avisar`
				// termina num pacote que nao volta.
				// ==================================================================================
				{
					ServerPlayer atirador = Forjar("ia: atuador", 50_000, new Vec2(0, 9216), comCerebro: false);

					// ============================ A COBAIA E O SOLAR FLARE, E NAO UM ID INVENTADO ============================
					// A tentacao era mandar "bancada_de_longe" pelo atuador e conferir a resposta
					// "habilidade desconhecida". Nao da: `Tecnicas.Get` NAO devolve nulo pra id novo --
					// ele CRIA uma entrada sintetica e a REGISTRA no catalogo (`Tecnicas.cs:60`), de
					// proposito, pra que uma skill nova nunca vire botao mudo. Numa bancada que roda em
					// servidor vivo isso deixaria uma tecnica fantasma no catalogo da sessao inteira.
					//
					// O Solar Flare serve melhor: e portado, instantaneo, o preco dele e publico
					// (`Tecnicas.SolarCustoKi`) e ele NAO esta na tabela de longe -- entao a IA nunca o
					// escolheria sozinha. E, antes de aprender a skill, ele da a recusa de verdade.
					// ==================================================================================================
					string? caminho = _skills?.Todas.FirstOrDefault(
						s => s.Verbos.Contains("Solar_Flare", StringComparer.OrdinalIgnoreCase))?.Path;

					if (caminho != null)
					{
						// 1. SEM SABER A TECNICA: o funil recusa, e a frase e a MESMA que o jogador leria.
						double antesDeSaber = atirador.Ficha.Ki;
						EscutaDeAvisos = [];
						AplicarComando(atirador, new Comando { Habilidade = "Solar_Flare" }, Protocol.TickSeconds);
						List<string> ditos = EscutaDeAvisos ?? [];
						EscutaDeAvisos = null;

						Checa("o pulso de tecnica atravessa o `UsarHabilidade` -- o MESMO do `C2S.Habilidade` -- "
							+ "e o JOGO recusa quem nao sabe a tecnica (o atuador nao conferiu nada)",
							  ditos.Any(d => d.Contains("nao sabe", StringComparison.OrdinalIgnoreCase)),
							  ditos.Count == 0 ? "silencio: o comando nao chegou no funil" : string.Join(" | ", ditos));
						Checa("...e recusado nao custa nada", Math.Abs(antesDeSaber - atirador.Ficha.Ki) < 1e-9);

						// 2. SABENDO: acontece e COBRA. Sem esta metade, "passou pelo funil" seria
						// compativel com um funil que nao faz nada.
						atirador.Livro.Dar(caminho);

						// ============================ O TANQUE TEM QUE CABER O PRECO ============================
						// `MaxKi` de um corpo de bancada (BP 50 mil, atributos 5) e MENOR que os
						// `100 * BaseDrain` que o Solar Flare cobra -- entao `Ki = MaxKi` fazia a
						// tecnica recusar com "isso pede pelo menos N de energia", e a bancada lia isso
						// como "nao cobrou". A recusa estava certa; o corpo e que era pequeno demais
						// pro gesto. Encher acima do preco e o que torna a MEDIDA possivel.
						// ==================================================================================
						double custo = Tecnicas.SolarCustoKi(atirador.Ficha);
						atirador.Ficha.Ki = Math.Max(atirador.Ficha.MaxKi, custo * 2);
						double antes = atirador.Ficha.Ki;

						EscutaDeAvisos = [];
						AplicarComando(atirador, new Comando { Habilidade = "Solar_Flare" }, Protocol.TickSeconds);
						List<string> resposta = EscutaDeAvisos ?? [];
						EscutaDeAvisos = null;

						Checa("...e uma tecnica que ele SABE e executada e COBRA o preco dela, igual a do jogador",
							  Math.Abs(antes - atirador.Ficha.Ki - custo) < custo * Folga + 0.01,
							  $"pagou {antes - atirador.Ficha.Ki:0.###}, devia pagar {custo:0.###}"
							  + (resposta.Count > 0 ? $" -- o jogo disse: {string.Join(" | ", resposta)}" : ""));
					}
					else Checa("a skill do Solar Flare foi achada no catalogo", false, "catalogo sem o verb");

					// A MIRA, PELO MESMO FUNIL DO CLIQUE DO JOGADOR -- inclusive a recusa dele.
					ServerPlayer mirado = Forjar("alvo: mirado", 50_000, new Vec2(80, 9216), comCerebro: false);
					AplicarComando(atirador, new Comando { Marcar = mirado.Id }, Protocol.TickSeconds);
					Checa("o comando de MIRAR marca pelo mesmo `Mirar` do `C2S.Alvo`",
						  atirador.AlvoId == mirado.Id, $"{atirador.AlvoId}");

					AplicarComando(atirador, new Comando { Marcar = 999_999 }, Protocol.TickSeconds);
					Checa("...e a validacao do funil vale pra IA tambem (mirar em quem nao existe limpa a mira)",
						  atirador.AlvoId == 0, $"{atirador.AlvoId}");
				}
			}

			// =====================================================================
			// 14. MOB-ZUMBI: um corpo que some no meio da volta nao derruba o tique
			// =====================================================================
			{
				ServerPlayer a = Forjar("ia: some", 50_000, new Vec2(0, 7168));
				ServerPlayer b = Forjar("ia: fica", 50_000, new Vec2(64, 7168));

				// tira o `a` do mundo NO MEIO da lista, como uma morte faria
				_players.Remove(a.Id);
				ZoneList(a.Zone.Hash).Remove(a);

				bool caiu = false;
				try { TickDosCorposSemDono(Protocol.TickSeconds); }
				catch { caiu = true; }

				Checa("um corpo removido no meio da volta nao derruba o tique (o `loc` a cada volta)",
					  !caiu);
				Checa("...e o corpo que ficou continua sendo dirigido", b.Cerebro != null);
				nascidos.Remove(a);
			}

			// =====================================================================
			// 15. PARIDADE DE CANAL **POR CONJUNTO** (varredura dos fontes)
			// =====================================================================
			// ============================ POR QUE ESTA SECAO NAO PODE SER UMA LISTA DE GESTOS ============================
			// As secoes 1, 4, 6, 7 e 9 provam, uma a uma, que voar/aparar/carregar/transformar/correr
			// custam o mesmo que custam ao jogador. Cada uma delas e verdadeira e nenhuma delas protege
			// o AMANHA: o proximo atalho vai ser um gesto que ainda nao existe, e ele nasce **fora** da
			// lista -- alguem escreve `npc.Voando = true` no atuador pra consertar um NPC que nao decola,
			// as 84 checagens continuam verdes, e o boneco na tela e identico ao certo.
			//
			// A unica forma de cobrir o que ainda nao foi escrito e afirmar sobre o CONJUNTO, e conjunto
			// de chamadas nao aparece em campo nenhum, tique nenhum, tela nenhuma: ou se le o fonte, ou a
			// frase "a IA passa pelos mesmos funis" e palavra minha. Sao duas afirmacoes:
			//
			//   (a) VERBOS  -- toda funcao que o ATUADOR chama, o funil do jogador tambem chama;
			//   (b) CAMPOS  -- todo campo de corpo que o atuador ESCREVE, o `Input` do jogador tambem
			//                  escreve. Esta e a que pega o atalho classico, porque um atalho quase nunca
			//                  e uma funcao nova: e uma atribuicao.
			//
			// E o limite dela, dito na cara: o conjunto do jogador e GRANDE (o `Handle` chama dezenas de
			// coisas), entao ela nao impede a IA de chamar um gesto legitimo que ninguem quis. Ela impede
			// o que importa -- sair do funil e mexer no corpo por baixo dele.
			// ======================================================================================================
			{
				string[] fonteIa = Fonte("Server/GameServer.Ia.cs");
				string[] fonteServidor = Fonte("Server/GameServer.cs");
				string[] fonteRaciais = Fonte("Server/GameServer.Raciais.cs");

				Checa("os tres fontes da paridade foram lidos do disco",
					  fonteIa.Length > 0 && fonteServidor.Length > 0 && fonteRaciais.Length > 0,
					  $"ia={fonteIa.Length} servidor={fonteServidor.Length} raciais={fonteRaciais.Length}");

				// ---- O ATUADOR: as DUAS unicas funcoes deste projeto que mexem no mundo pela IA ----
				// `LerCapacidades`, `LerPercepcao` e `ArsenalDeLonge` ficam FORA de proposito: elas so
				// PERGUNTAM. Uma leitura pode chamar o que quiser -- o que nao pode e agir por fora.
				string[] corpoAtuador =
					[.. CorpoDoMetodo(fonteIa, "private void AplicarComando"),
					 .. CorpoDoMetodo(fonteIa, "private void PassoDaIa")];

				Checa("o corpo do atuador foi extraido dos fontes (`AplicarComando` + `PassoDaIa`)",
					  corpoAtuador.Length > 30, $"{corpoAtuador.Length} linhas");

				// ---- O FUNIL DO JOGADOR: os tres pontos por onde uma tecla vira gesto ----
				// `Handle` (o switch dos C2S), `Input` (o InputState) e `UsarHabilidade` (o canal de
				// habilidade, que e por onde o verbo `Fly` do jogador chama o `AlternarVoo`).
				string[] corpoJogador =
					[.. CorpoDoMetodo(fonteServidor, "private void Handle(NetPeer"),
					 .. CorpoDoMetodo(fonteServidor, "private void Input(NetPeer"),
					 .. CorpoDoMetodo(fonteRaciais, "private void UsarHabilidade(ServerPlayer")];

				Checa("o corpo do funil do jogador foi extraido (`Handle` + `Input` + `UsarHabilidade`)",
					  corpoJogador.Length > 200, $"{corpoJogador.Length} linhas");

				// ============================ AS TRES EXCECOES, E CADA UMA TEM ARGUMENTO ============================
				//   * `PassoDaIa`   -- e o proprio atuador chamando a outra metade de si mesmo.
				//   * `Advance`     -- **a assimetria de propósito do movimento**: o jogador vai por
				//     `MoveRules.ValidateStep`, que CONFERE uma posicao que o cliente afirmou; a IA vai
				//     por `MoveRules.Advance`, que GERA a posicao. Sao perguntas opostas (`MoveRules.cs:91`
				//     vs `:159`) e uni-las daria uma funcao com dois modos e um `if`. As POLITICAS -- que
				//     sao o que da pra compartilhar -- ja sao as mesmas, e sao elas que cobram.
				//   * `FacingFrom` -- o jogador manda o `facing` no pacote (dois bits do `InputState`) e a
				//     IA manda no `Comando.Olhar`; os dois passam pela MESMA quantizacao pras quatro
				//     direcoes do BYOND. A assimetria e so o transporte -- um vem da rede, o outro nao
				//     tem rede. Funcao pura, sem estado e sem custo.
				// ==============================================================================================
				string[] excecoesDeChamada = ["PassoDaIa", "Advance", "FacingFrom"];

				HashSet<string> chamaIa = ChamadasDe(corpoAtuador);
				HashSet<string> chamaJogador = ChamadasDe(corpoJogador);
				List<string> foraDoFunil =
					[.. chamaIa.Where(n => !chamaJogador.Contains(n) && Array.IndexOf(excecoesDeChamada, n) < 0)
							   .OrderBy(n => n)];

				Checa($"(a) VERBOS: as {chamaIa.Count} funcoes que o atuador chama, o funil do jogador "
					+ "tambem chama -- fora as tres assimetrias argumentadas",
					  foraDoFunil.Count == 0, string.Join(", ", foraDoFunil));

				// as tres excecoes tem que estar MESMO la: uma excecao que nao e usada e uma porta
				// aberta esquecida, e a proxima pessoa acha que ela cobre outra coisa.
				Checa("...e as tres excecoes escritas sao as tres que existem de verdade",
					  excecoesDeChamada.All(chamaIa.Contains),
					  string.Join(", ", excecoesDeChamada.Where(e => !chamaIa.Contains(e))));

				HashSet<string> escreveIa = EscritasEm(corpoAtuador, "npc", "pl");
				HashSet<string> escreveJogador = EscritasEm(CorpoDoMetodo(fonteServidor, "private void Input(NetPeer"), "pl");
				List<string> escritaProibida =
					[.. escreveIa.Where(c => !escreveJogador.Contains(c)).OrderBy(c => c)];

				Checa($"(b) CAMPOS: os {escreveIa.Count} campos de corpo que o atuador escreve sao os "
					+ "MESMOS que o `Input` do jogador escreve -- nenhum estado tocado por baixo do funil",
					  escritaProibida.Count == 0, string.Join(", ", escritaProibida));

				// ---- (c) O CEREBRO NAO TEM MAOS ----
				// A trava estrutural, e a mais barata das tres: se o `Core/Ai` nao conhece `ServerPlayer`
				// nem `GameServer`, nenhuma decisao PODE mexer no mundo -- ela so sabe devolver um
				// `Comando`. Um `using Jandirus.Server` ali dentro seria o fim de todo o resto.
				var maosNoCore = new List<string>();
				string pastaIa = Godot.ProjectSettings.GlobalizePath("res://Core/Ai");
				if (System.IO.Directory.Exists(pastaIa))
					foreach (string arq in System.IO.Directory.EnumerateFiles(pastaIa, "*.cs"))
						foreach (string cru in System.IO.File.ReadAllLines(arq))
						{
							string l = SemTextoNemComentario(cru).Trim();
							if (l.Contains("ServerPlayer") || l.Contains("GameServer")
								|| l.Contains("Jandirus.Server"))
								maosNoCore.Add(System.IO.Path.GetFileName(arq));
						}
				Checa("(c) o cerebro NAO TEM MAOS: nada em `Core/Ai` conhece `ServerPlayer`, `GameServer` "
					+ "ou o namespace do servidor -- ele so sabe devolver um `Comando`",
					  maosNoCore.Count == 0, string.Join(", ", maosNoCore.Distinct()));

				// ---- (d) QUEM FABRICA CEREBRO -- o marcador do tempero ----
				// Tres lugares criam cerebro hoje: o clone da mente, a fera e a furia lendaria. Os dois
				// ultimos temperam (`Disciplina = 0`, `Inteligencia = 0`); um QUARTO lugar que nascesse
				// com o default viraria um possuido que APARA e RECARREGA, e ninguem ligaria isso a este
				// arquivo. Quando o quarto chegar (e ele vai: a proxima posse), esta linha reprova e a
				// conversa acontece -- que e o ponto de um marcador.
				string[] podemFabricar = ["GameServer.Clone.cs", "GameServer.Oozaru.cs", "GameServer.FuriaLendaria.cs"];
				var fabricas = new List<string>();
				foreach (string dir in new[] { "Core", "Server", "Client" })
				{
					string caminho = Godot.ProjectSettings.GlobalizePath("res://" + dir);
					if (!System.IO.Directory.Exists(caminho)) continue;
					foreach (string arq in System.IO.Directory.EnumerateFiles(caminho, "*.cs", System.IO.SearchOption.AllDirectories))
					{
						string nome = System.IO.Path.GetFileName(arq);
						if (nome.Contains("Teste")) continue;   // bancada fabrica cerebro por oficio
						foreach (string cru in System.IO.File.ReadAllLines(arq))
						{
							string l = SemTextoNemComentario(cru);
							if (System.Text.RegularExpressions.Regex.IsMatch(l, @"Cerebro\s*=\s*new")
								&& Array.IndexOf(podemFabricar, nome) < 0)
								fabricas.Add(nome);
						}
					}
				}
				Checa("(d) so tres arquivos de producao FABRICAM cerebro (clone, fera, furia) -- um quarto "
					+ "nasceria com o tempero default, que APARA e RECARREGA",
					  fabricas.Count == 0, string.Join(", ", fabricas.Distinct()));

				// ============================ E AGORA A PROVA DE QUE ISTO REPROVA ============================
				// As quatro checagens acima estao VERDES. Verde num teste que le fonte nao significa nada
				// enquanto ninguem viu o vermelho: um erro de regex, um extrator que devolveu zero linha,
				// um `Contains` invertido -- todos passam calados pra sempre.
				//
				// Entao o defeito e INJETADO: as mesmas duas varreduras rodam sobre uma COPIA do fonte do
				// atuador com o atalho classico escrito dentro dele. Nada e gravado em disco; o que muda e
				// o array de linhas na memoria.
				// ======================================================================================
				{
					int ondeEnfiar = Array.FindIndex(fonteIa, l => l.Contains("private void AplicarComando"));
					var adulterado = new List<string>(fonteIa);
					if (ondeEnfiar >= 0)
					{
						// depois da assinatura vem a `{`; o atalho entra logo abaixo dela
						adulterado.Insert(ondeEnfiar + 2, "\t\tnpc.Voando = true;");
						adulterado.Insert(ondeEnfiar + 3, "\t\tnpc.Ficha.Ki -= 10;");
						adulterado.Insert(ondeEnfiar + 4, "\t\tMeleeResolver.Resolver(npc, npc);");
					}

					string[] atuadorFalso =
						[.. CorpoDoMetodo([.. adulterado], "private void AplicarComando"),
						 .. CorpoDoMetodo([.. adulterado], "private void PassoDaIa")];

					List<string> chamadaFlagrada =
						[.. ChamadasDe(atuadorFalso)
								.Where(n => !chamaJogador.Contains(n) && Array.IndexOf(excecoesDeChamada, n) < 0)];
					List<string> escritaFlagrada =
						[.. EscritasEm(atuadorFalso, "npc", "pl").Where(c => !escreveJogador.Contains(c))];

					Checa("DEFEITO INJETADO: com `MeleeResolver.Resolver(...)` enfiado no atuador, a "
						+ "varredura (a) REPROVA e diz o nome da funcao",
						  chamadaFlagrada.Contains("Resolver"), string.Join(", ", chamadaFlagrada));
					Checa("DEFEITO INJETADO: com `npc.Voando = true` e `npc.Ficha.Ki -= 10` enfiados, a "
						+ "varredura (b) REPROVA os dois -- e sao os dois atalhos mais tentadores que existem",
						  escritaFlagrada.Contains("Voando") && escritaFlagrada.Contains("Ficha.Ki"),
						  string.Join(", ", escritaFlagrada));

					// E O CONTROLE DO INJETOR: sem a adulteracao, as mesmas contas dao vazio. Sem esta
					// linha, um extrator que devolvesse lixo daria "reprovou" nas duas de cima e eu leria
					// isso como sucesso.
					Checa("...e sem a adulteracao as MESMAS duas contas dao vazio (o injetor e que mudou, "
						+ "nao a conta)",
						  foraDoFunil.Count == 0 && escritaProibida.Count == 0);
				}
			}

			// =====================================================================
			// 16. A OUTRA METADE DA CARGA: ela NAO carrega no momento errado
			// =====================================================================
			// A secao 6 prova o sentido facil (com o tanque no fundo e o alvo longe, ela planta o pe e
			// respira) e a interrupcao. O sentido dificil e este: um NPC que recarrega quando NAO
			// precisa e o tell mais barato de robo que existe -- humano nenhum senta pra meditar de
			// tanque cheio no meio de uma troca de socos.
			{
				var c = new Cerebro
				{
					Inteligencia = 1.0,   // a mais propensa a recarregar: se alguem carrega errado, e ela
					Poderes = new Capacidades { SabeReunirKi = true, TemComQueAparar = true },
				};
				var rng = new Random(1616);

				int carregouDeTanqueCheio = 0, carregouColado = 0;
				for (int i = 0; i < 3000; i++)
				{
					// TANQUE CHEIO, alvo longe: a distancia CONVIDA (e a mesma da secao 6), o tanque nao.
					var farto = new Percepcao
					{
						TemAlvo = true, Minha = Vec2.Zero, DoAlvo = new Vec2(900, 0),
						VidaFrac = 1, KiFrac = 1, Ki = 5000, FolegoFrac = 1,
						MeuPoder = 1000, PoderDoAlvo = 1000,
					};
					if (c.Pensar(farto, Protocol.TickSeconds, rng).Carregar) carregouDeTanqueCheio++;
				}
				Checa("de tanque cheio ela NUNCA para pra respirar, nem com o alvo longe (3000 tiques)",
					  carregouDeTanqueCheio == 0, $"{carregouDeTanqueCheio} tiques carregando");

				var c2 = new Cerebro
				{
					Inteligencia = 1.0,
					Poderes = new Capacidades { SabeReunirKi = true, TemComQueAparar = true },
				};
				for (int i = 0; i < 3000; i++)
				{
					// TANQUE NO FUNDO, mas com o inimigo COLADO: o `rechargeState` do DM manda recuar
					// ate 6 tiles ANTES de plantar o pe -- carregar com o cara na cara e suicidio, e a
					// mesma interrupcao que a secao 6 mede depois de comecada.
					var acuado = new Percepcao
					{
						TemAlvo = true, Minha = Vec2.Zero, DoAlvo = new Vec2(24, 0),
						VidaFrac = 0.9, KiFrac = 0.03, Ki = 5, FolegoFrac = 0.05,
						MeuPoder = 1000, PoderDoAlvo = 1000,
					};
					if (c2.Pensar(acuado, Protocol.TickSeconds, rng).Carregar) carregouColado++;
				}
				Checa("...e com o tanque no fundo E o inimigo colado ela tambem nao carrega: primeiro "
					+ "abre distancia (o `NPC_AI_RECHARGE_DIST` de 6 tiles)",
					  carregouColado == 0, $"{carregouColado} tiques carregando a 24 px do inimigo");

				// E O CONTROLE, que e o que da sentido aos dois zeros de cima: MESMO cerebro, MESMO
				// tanque no fundo, so a distancia mudou. Sem esta linha os dois zeros seriam compativeis
				// com uma IA que simplesmente nao sabe carregar.
				var c3 = new Cerebro
				{
					Inteligencia = 1.0,
					Poderes = new Capacidades { SabeReunirKi = true, TemComQueAparar = true },
				};
				int carregouLonge = 0;
				for (int i = 0; i < 3000; i++)
				{
					var longe = new Percepcao
					{
						TemAlvo = true, Minha = Vec2.Zero, DoAlvo = new Vec2(900, 0),
						VidaFrac = 0.9, KiFrac = 0.03, Ki = 5, FolegoFrac = 0.05,
						MeuPoder = 1000, PoderDoAlvo = 1000,
					};
					if (c3.Pensar(longe, Protocol.TickSeconds, rng).Carregar) carregouLonge++;
				}
				Checa("(controle) com o MESMO tanque no fundo e o alvo LONGE ela carrega -- entao os dois "
					+ "zeros acima sao recusa, e nao incapacidade",
					  carregouLonge > 100, $"{carregouLonge} tiques carregando");
			}

			// =====================================================================
			// 17. VINTE NPCs NUMA ZONA, POR MINUTOS -- o custo nao cresce com o TEMPO
			// =====================================================================
			// ============================ UM PONTO NAO MEDE ISTO, E A SECAO 12 SO MEDE PONTOS ============================
			// A secao 12 responde "quanto custa um tique?" com 20 corpos e da 40 us. O mob-zumbi nao
			// aparece la e nunca apareceria: ele nao e um tique caro, e um tique que fica caro. Lista que
			// so cresce, corpo morto que ninguem tira, assinatura que ninguem cancela -- todos custam
			// quase nada no minuto 1 e derrubam a zona no minuto 20. Foi assim no DM (`NPCAI.dm:751`) e
			// foi assim aqui, com as 19 assinaturas orfas por ciclo de relog.
			//
			// O EIXO CERTO E O TIQUE, E NAO O RELOGIO DE PAREDE: o estado que vaza vaza POR TIQUE. Entao
			// 5400 tiques -- tres minutos de jogo a 30 Hz -- em seis janelas, e a pergunta e se a ultima
			// custa mais que a primeira. (Em tempo de parede isto leva segundos, e e a mesma coisa.)
			// ======================================================================================================
			{
				// A ZONA TEM QUE ESTAR LIMPA -- mesmo argumento da secao 12: os corpos das secoes
				// anteriores continuam no mundo e brigariam durante a medida.
				foreach (ServerPlayer velho in _players.Values) velho.Cerebro = null;

				var vinte = new List<ServerPlayer>();
				for (int i = 0; i < 20; i++)
				{
					// PERTO E BRIGANDO, que e o cenario do dono ("20 NPCs numa zona"). A secao 12 os
					// afasta de proposito pra medir SO a IA; aqui o que se mede e a zona inteira ao
					// longo do tempo, e um tique de mentira nao vaza nada.
					ServerPlayer c = Forjar($"ia: horda{i}", 50_000, new Vec2(10240 + i % 5 * 48, 10240 + i / 5 * 48));
					EnsinarAVoar(c);
					c.Ficha.MeditateGivesKiRegen = 1;
					c.Combate.Letal = false;
					c.Cerebro!.Poderes = LerCapacidades(c);
					vinte.Add(c);
				}

				/// os corpos de pe e com folego -- FORA da janela cronometrada, e igual em toda janela
				void Reanimar()
				{
					foreach (ServerPlayer c in vinte)
					{
						c.Combate.Corpo.Restaurar();
						c.Combate.SincronizarVida();
						c.Ficha.KO = false; c.Ficha.dead = false;
						c.Ficha.Ki = c.Ficha.MaxKi;
						c.Ficha.stamina = c.Ficha.maxstamina;
					}
				}

				const int janelas = 6, tiquesPorJanela = 900;   // 5400 tiques = 3 min de jogo a 30 Hz
				var us = new double[janelas];
				var lixo = new double[janelas];

				for (int j = 0; j < janelas; j++)
				{
					double soma = 0; long bytes = 0;
					for (int t = 0; t < tiquesPorJanela; t++)
					{
						if (t % 30 == 0) Reanimar();   // fora da medida: identico em todas as janelas

						long b0 = GC.GetAllocatedBytesForCurrentThread();
						ulong t0 = Time.GetTicksUsec();
						TickCombate(Protocol.TickSeconds);
						TickDosCorposSemDono(Protocol.TickSeconds);
						soma += Time.GetTicksUsec() - t0;
						bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
					}
					us[j] = soma / tiquesPorJanela;
					lixo[j] = bytes / (double)tiquesPorJanela;
				}

				GD.Print("  ---- 20 corpos numa zona, 5400 tiques (3 min de jogo), por janela de 900 ----");
				for (int j = 0; j < janelas; j++)
					GD.Print($"       janela {j + 1}: {us[j]:0.0} us/tique, {lixo[j]:0} B/tique");

				// ============================ O CRITERIO ============================
				// Nao e "a ultima janela e menor que a primeira" -- ruido de maquina inverte isso sozinho.
				// E o TERCO FINAL contra o TERCO INICIAL, com uma folga generosa (1,5x) e um piso
				// absoluto pra que numeros pequenos nao virem alarme por arredondamento. Vazamento de
				// verdade nao cresce 50%: ele cresce ordens de grandeza.
				// ==================================================================
				static bool Progrediu(double[] j)
				{
					int terco = Math.Max(1, j.Length / 3);
					double inicio = j.Take(terco).Average(), fim = j.Skip(j.Length - terco).Average();
					return fim > inicio * 1.5 + 10;
				}

				Checa($"o tique NAO fica mais caro com o tempo (janela 1 {us[0]:0.0} us -> janela "
					+ $"{janelas} {us[^1]:0.0} us, 5400 tiques com 20 corpos brigando)",
					  !Progrediu(us), string.Join(" -> ", us.Select(v => $"{v:0.0}")));
				Checa("...e o LIXO por tique tambem nao (vazamento aparece antes no coletor que no relogio)",
					  !Progrediu(lixo), string.Join(" -> ", lixo.Select(v => $"{v:0}")));

				// ---- E O MOB-ZUMBI EM SI: as contagens tem que estar de pe no fim ----
				int aindaDirigidos = vinte.Count(c => c.Cerebro != null);
				int aindaNoMundo = vinte.Count(c => _players.ContainsKey(c.Id));
				Checa("os 20 continuam sendo dirigidos depois de 3 min (nenhum virou estatua calado)",
					  aindaDirigidos == 20, $"{aindaDirigidos} de 20");
				Checa("...e os 20 continuam no mundo (nenhum sumiu da zona e ficou preso em lista)",
					  aindaNoMundo == 20, $"{aindaNoMundo} de 20");
				Checa("...e a lista de dirigidos nao inchou (ela e REUSADA, e `Clear` nao a faz crescer)",
					  _dirigidos.Count == aindaDirigidos, $"{_dirigidos.Count} contra {aindaDirigidos}");
				Checa("...e a lista da zona tem exatamente os corpos que existem",
					  ZoneList(zona.Hash).Count == _players.Values.Count(p => p.Zone.Hash == zona.Hash),
					  $"{ZoneList(zona.Hash).Count}");

				// ============================ E O CRITERIO TEM DENTES? INJETA-SE O VAZAMENTO ============================
				// Um "nao cresceu" so vale se um "cresceu" for detectavel pelo MESMO instrumento. Entao
				// aqui roda a mesma medida com um mob-zumbi de verdade enfiado dentro da janela: uma lista
				// que ganha corpos a cada tique e que e VARRIDA a cada tique -- que e literalmente a forma
				// do defeito que o DM pagou. Se o `Progrediu` nao acusar isto, ele nao acusaria nada.
				// ==================================================================================================
				{
					var zumbis = new List<ServerPlayer>();
					var comVazamento = new double[janelas];
					long lixeira = 0;
					for (int j = 0; j < janelas; j++)
					{
						double soma = 0;
						for (int t = 0; t < 150; t++)
						{
							ulong t0 = Time.GetTicksUsec();
							TickDosCorposSemDono(Protocol.TickSeconds);
							// O VAZAMENTO: 60 corpos a mais por tique, e todos varridos.
							for (int k = 0; k < 60; k++) zumbis.Add(vinte[k % vinte.Count]);
							foreach (ServerPlayer z in zumbis) lixeira += z.Id;
							soma += Time.GetTicksUsec() - t0;
						}
						comVazamento[j] = soma / 150.0;
					}
					_ = lixeira;

					GD.Print($"       (injetado) com mob-zumbi: {string.Join(" -> ", comVazamento.Select(v => $"{v:0.0}"))} us");
					Checa("DEFEITO INJETADO: com uma lista que so cresce e e varrida todo tique, o MESMO "
						+ "criterio REPROVA -- entao o verde de cima e uma medida, e nao um teto largo",
						  Progrediu(comVazamento),
						  string.Join(" -> ", comVazamento.Select(v => $"{v:0.0}")));
				}

				foreach (ServerPlayer c in vinte) c.Cerebro = null;   // a proxima secao mede outra coisa
			}

			// =====================================================================
			// 18. UMA EXCECAO NO CEREBRO NAO MORRE CALADA
			// =====================================================================
			// ============================ O DEFEITO QUE ESTA SECAO EXISTE PRA IMPEDIR ============================
			// Um `catch` em volta do tique inteiro faz o servidor "continuar funcionando" com a luta toda
			// errada. Um `catch` vazio por corpo faz o NPC virar estatua sem nada no console. Os dois
			// parecem estabilidade e sao a mesma doenca: o defeito continua acontecendo e ninguem sabe.
			// Este projeto ja viveu a versao DM disto -- runtimes da genetica matando a IA em silencio
			// ate alguem por o DEBUG.log em disco.
			//
			// O contrato aqui e: o corpo e SOLTO (o cerebro sai, o input morre), o nome dele e a excecao
			// vao pro console, e o resto da zona nao percebe. As tres coisas sao medidas, e a do console
			// e lida no FONTE -- `GD.PushError` termina no log do engine e nao volta pra dentro do
			// processo; ou se le o fonte, ou "aparece em algum lugar" e palavra minha.
			// ================================================================================================
			{
				// ---- A INJECAO ----
				// Um corpo com o LIVRO arrancado. `LerCapacidades` -> `PodeVoar` -> `NivelDeVoo` ->
				// `pl.Livro.Sabe(...)` explode, e isso e uma excecao NO CAMINHO DA DECISAO -- que e o
				// caso que o contrato promete cobrir. (Um cerebro recem-nascido le capacidades no
				// PRIMEIRO tique dele: o relogio de 1 Hz comeca zerado.)
				ServerPlayer quebrado = Forjar("ia: quebrado", 50_000, new Vec2(0, 12288));
				ServerPlayer vizinho = Forjar("ia: vizinho sao", 50_000, new Vec2(400, 12288));
				vizinho.Cerebro!.Poderes = LerCapacidades(vizinho);

				// ============================ O VIZINHO PRECISA DE UM GESTO QUE ACONTECA EM **TODO** TIQUE ============================
				// A afirmacao e "a volta continuou DEPOIS do `catch`, no MESMO tique" -- e ela so se prova
				// com um observavel que existe em todo tique. Perseguir nao serve: escrita assim, esta
				// checagem reprovou com "andou=0", e a causa nao era a volta ter morrido -- o vizinho
				// tinha ALCANCADO a presa e estava socando, parado, que e o certo.
				//
				// Sem presa, o plano e `Vagar` e o passo e todo tique (a secao 19 afirma 300 de 300). Pra
				// isso ele vai pra uma zona vazia, que de quebra nao tem mapa de colisao -- entao nem
				// parede pode roubar um passo e transformar um teste de VOLTA num teste de terreno.
				// ================================================================================================================
				ZoneList(vizinho.Zone.Hash).Remove(vizinho);
				vizinho.Zone = ZoneKey.Premade("bancada: zona vazia da ia");
				ZoneList(vizinho.Zone.Hash).Add(vizinho);

				Checa("(preparo) os dois nascem dirigidos", quebrado.Cerebro != null && vizinho.Cerebro != null);

				// ============================ E A ORDEM DA VOLTA E METADE DA AFIRMACAO ============================
				// Se o vizinho fosse ticado ANTES do corpo que explode, ele andaria de qualquer jeito e o
				// verde nao diria nada. `_dirigidos` e montado na ordem de `_players.Values`, entao e essa
				// a ordem que precisa ser conferida -- e nao a ordem em que eu escrevi as duas linhas.
				// ============================================================================================
				var ordem = _players.Values.Select(p => p.Id).ToList();
				Checa("(preparo) o corpo que vai explodir vem ANTES do vizinho na volta -- senao o verde "
					+ "de baixo seria de graca",
					  ordem.IndexOf(quebrado.Id) >= 0 && ordem.IndexOf(quebrado.Id) < ordem.IndexOf(vizinho.Id),
					  $"quebrado@{ordem.IndexOf(quebrado.Id)} vizinho@{ordem.IndexOf(vizinho.Id)}");

				// ============================ O AQUECIMENTO NAO E ENFEITE ============================
				// Escrita sem ele, a checagem do vizinho reprovou com "andou=0 px" -- e a leitura obvia
				// (a volta morreu na primeira excecao) estava ERRADA. Um cerebro RECEM-NASCIDO esta no
				// plano `Nada`, e o compromisso (`TempoMinimoNoPlano`, 1,2 s) segura o primeiro plano
				// tanto quanto segura os outros: no tique 1 ninguem anda, quebrado ou nao. E a mesma
				// licao que a secao 13 ja tinha aprendido -- **um teste que prepara o estado por
				// contagem de tiques esta chutando**; entao aqui ele AFIRMA o estado antes de mexer.
				//
				// O aquecimento tambem move o relogio de 1 Hz das capacidades, que e onde a leitura
				// explode. Por isso a quebra e esperada em ATE 90 tiques, e nao num tique escolhido a
				// dedo: o que importa nao e QUANDO ela vem, e o que acontece no tique em que ela vem.
				// ================================================================================
				Vec2 antesDoAquecimento = vizinho.Pos;
				for (int i = 0; i < 45; i++) TickDosCorposSemDono(Protocol.TickSeconds);
				Checa("(preparo) o vizinho ja esta VAGANDO e andando ANTES da injecao",
					  vizinho.Cerebro!.Atual == Plano.Vagar
					  && (vizinho.Pos - antesDoAquecimento).LengthSquared > 0.01f,
					  $"plano={vizinho.Cerebro.Atual} andou={(vizinho.Pos - antesDoAquecimento).Length:0.#} px");

				quebrado.Livro = null!;

				bool derrubouOTique = false, vizinhoAndouNoTiqueDaQuebra = false;
				int tiquesAteQuebrar = 0;
				while (quebrado.Cerebro != null && tiquesAteQuebrar < 90)
				{
					Vec2 antesDoVizinho = vizinho.Pos;
					try { TickDosCorposSemDono(Protocol.TickSeconds); }
					catch { derrubouOTique = true; break; }
					tiquesAteQuebrar++;
					// o ULTIMO tique desta volta e o tique em que o corpo quebrado foi solto
					vizinhoAndouNoTiqueDaQuebra = (vizinho.Pos - antesDoVizinho).LengthSquared > 0.01f;
				}

				Checa("uma excecao no cerebro NAO derruba o tique (o `try` e POR CORPO)", !derrubouOTique);
				Checa("...e o corpo quebrado e SOLTO -- ele para de ser dirigido em vez de explodir "
					+ "30 vezes por segundo pra sempre",
					  quebrado.Cerebro == null);
				Checa("...e o input dele morre junto (`LargarOInput`: nada fica pendurado)",
					  !quebrado.Moving && !quebrado.Correndo && !quebrado.Ficha.dashing
					  && !quebrado.QuerSubir && !quebrado.QuerDescer && !quebrado.Carregando
					  && !quebrado.Combate.Bloqueando);
				Checa("...e o VIZINHO continua dirigido e ANDOU NO MESMO TIQUE em que o outro explodiu "
					+ "(a volta seguiu depois do `catch`, e nao morreu junto com o primeiro corpo)",
					  vizinho.Cerebro != null && vizinhoAndouNoTiqueDaQuebra,
					  $"cerebro={vizinho.Cerebro != null} andou={vizinhoAndouNoTiqueDaQuebra} "
					+ $"({tiquesAteQuebrar} tiques ate a quebra)");

				// O CONTROLE: sem a injecao, ninguem e solto. Sem esta linha, um tique que soltasse TODO
				// mundo (por qualquer outro motivo) daria verde nas checagens de cima.
				ServerPlayer sao = Forjar("ia: controle", 50_000, new Vec2(0, 12480));
				sao.Cerebro!.Poderes = LerCapacidades(sao);
				for (int i = 0; i < 60; i++) TickDosCorposSemDono(Protocol.TickSeconds);
				Checa("(controle) um corpo INTEIRO atravessa 60 tiques sem ser solto -- entao soltar e "
					+ "consequencia da excecao, e nao do tique",
					  sao.Cerebro != null);

				// ---- E O CONSOLE, LIDO NO FONTE ----
				string[] fonteClone = Fonte("Server/GameServer.Clone.cs");
				string[] laco = CorpoDoMetodo(fonteClone, "private void TickDosCorposSemDono");

				int ondeForeach = Array.FindIndex(laco, l => l.Contains("foreach"));
				int ondeTry = Array.FindIndex(laco, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\btry\b"));

				// ============================ ESTA E A UNICA CHECAGEM QUE LE O FONTE **CRU** ============================
				// E ela reprovou na primeira rodada por isso: o `SemTextoNemComentario` arranca o texto
				// entre aspas -- que e exatamente onde a mensagem mora --, e o `GD.PushError($"...{ex}")`
				// chegava aqui como `GD.PushError($)`. A limpeza esta certa pra contar CHAMADA e ESCRITA
				// (senao o cabecalho do proprio arquivo, que cita `npc.Combate.Guardar(bool)` por
				// extenso, viraria uma chamada). Pra ler o que a mensagem DIZ, a linha crua e a fonte.
				// ==================================================================================================
				int inicioCru = Array.FindIndex(fonteClone, l => l.Contains("private void TickDosCorposSemDono"));
				string capturado = inicioCru < 0 ? ""
					: string.Join(" ", fonteClone.Skip(inicioCru).Take(60).SkipWhile(l => !l.Contains("catch")));

				Checa("o `try` mora DENTRO da volta (por corpo), e nao em volta do tique",
					  ondeForeach >= 0 && ondeTry > ondeForeach, $"foreach@{ondeForeach} try@{ondeTry}");
				Checa("...e o `catch` NAO e vazio: ele despeja a excecao com o nome e o id do corpo",
					  capturado.Contains("PushError") && capturado.Contains("{ex}")
					  && capturado.Contains("npc.Name") && capturado.Contains("npc.Id"), capturado);
				Checa("...e ele SOLTA o corpo em vez de deixa-lo tentando de novo",
					  capturado.Contains("Cerebro = null") && capturado.Contains("LargarOInput"), capturado);

				// ============================ E O QUE ESTE CONTRATO NAO COBRE, DITO NA CARA ============================
				// A recuperacao (`LargarOInput`) toca `Ficha` e `Combate`. Se a corrupcao estiver
				// exatamente nesses dois, o proprio `catch` explode e a excecao escapa da volta -- o
				// servidor cai. Nao e caso de producao (nenhum caminho deixa `Ficha` nula) e consertar
				// seria por um `try` dentro do `catch`, que e o tipo de rede que esconde defeito de
				// verdade. A checagem AFIRMA o limite: se um dia alguem blindar a recuperacao, ela
				// reprova e a conversa acontece.
				// ==============================================================================================
				// SOZINHO NA VOLTA, e nao por delicadeza: um corpo com `Ficha` nula tambem explode o
				// `PresaDaFera` de TODO vizinho da zona (ele varre `o.Ficha.dead`), e ai o que a checagem
				// mediria seria a excecao dos outros -- pelo caminho que ja foi provado logo acima.
				foreach (ServerPlayer velho in _players.Values) velho.Cerebro = null;

				ServerPlayer pior = Forjar("ia: corrupto", 50_000, new Vec2(0, 12672));
				pior.Ficha = null!;
				bool escapou = false;
				try { TickDosCorposSemDono(Protocol.TickSeconds); }
				catch { escapou = true; }
				Checa("LIMITE AFIRMADO: se a corrupcao esta na `Ficha`/`Combate` -- o que a propria "
					+ "recuperacao toca -- a excecao ESCAPA da volta. Nao ha caminho de producao que "
					+ "produza isso; se esta linha reprovar, alguem blindou o `catch` e a checagem vira",
					  escapou);
				// o corpo corrompido nao pode ficar no mundo pro `finally` tropecar nele
				_players.Remove(pior.Id);
				ZoneList(pior.Zone.Hash).Remove(pior);
				nascidos.Remove(pior);

				quebrado.Cerebro = null; vizinho.Cerebro = null; sao.Cerebro = null;
			}

			// =====================================================================
			// 19. OOZARU E FURIA LENDARIA: o tempero vem da PRODUCAO
			// =====================================================================
			// ============================ A SECAO 10 MEDE UM CEREBRO QUE EU MESMO TEMPEREI ============================
			// Ela monta um `Cerebro { Disciplina = 0, Inteligencia = 0 }` na mao e prova que ASSIM
			// temperado o bicho nao apara e nao recarrega. Isso e verdade e nao e a pergunta: a pergunta e
			// se o `TomarAsRedeas` do Oozaru e o `TomarAsRedeasDaFuria` **continuam pondo esses valores**.
			// Alguem apaga uma linha la, a secao 10 continua verde, e o macaco passa a bloquear no ritmo
			// do oponente -- o oposto de um desastre, e sem nada na tela explicando.
			//
			// Aqui as duas funcoes de PRODUCAO sao chamadas, e o cerebro medido e o que elas produziram.
			// ====================================================================================================
			{
				ServerPlayer macaco = Forjar("ia: fera de verdade", 50_000, new Vec2(0, 13000), comCerebro: false);
				TomarAsRedeas(macaco);

				Checa("o `TomarAsRedeas` do Oozaru entrega um corpo dirigido", macaco.Cerebro != null);
				Cerebro fera = macaco.Cerebro!;
				Checa("...com `VidaCautelosa = 0` (ela nao se preserva)", fera.VidaCautelosa == 0, $"{fera.VidaCautelosa}");
				Checa("...`ChanceDePesado = 0,85` (braco de macaco gigante nao da jab)",
					  Math.Abs(fera.ChanceDePesado - 0.85) < 1e-9, $"{fera.ChanceDePesado}");
				Checa("...`IntervaloDeDecisao = 0,5` (pesada e lenta pra mudar de ideia)",
					  Math.Abs(fera.IntervaloDeDecisao - 0.5) < 1e-9, $"{fera.IntervaloDeDecisao}");
				Checa("...`Disciplina = 0` -- a manivela NOVA: sem ela o macaco passa a APARAR",
					  fera.Disciplina == 0, $"{fera.Disciplina}");
				Checa("...e `Inteligencia = 0` -- sem ela ele RECARREGA, paira rasante e decide subir de forma",
					  fera.Inteligencia == 0, $"{fera.Inteligencia}");
				Checa("...e o input do dono foi largado no mesmo gesto (`LargarOInput`)",
					  !macaco.Moving && !macaco.Correndo && !macaco.Ficha.dashing
					  && !macaco.QuerSubir && !macaco.QuerDescer && !macaco.Carregando);

				ServerPlayer lendario = Forjar("ia: furia de verdade", 50_000, new Vec2(0, 13200), comCerebro: false);
				TomarAsRedeasDaFuria(lendario, null, 0);

				Checa("o `TomarAsRedeasDaFuria` entrega um corpo dirigido", lendario.Cerebro != null);
				Cerebro furia = lendario.Cerebro!;
				Checa("...com `VidaCautelosa = 0` (furia que se protege nao e furia)",
					  furia.VidaCautelosa == 0, $"{furia.VidaCautelosa}");
				Checa("...`IntervaloDeDecisao = 0,2` -- o `LEGB_TICK 2` do DM, LITERAL",
					  Math.Abs(furia.IntervaloDeDecisao - 0.2) < 1e-9, $"{furia.IntervaloDeDecisao}");
				Checa("...`ChanceDePesado = 0,6` (entre o clone que mede o golpe e a fera que nao mede)",
					  Math.Abs(furia.ChanceDePesado - 0.6) < 1e-9, $"{furia.ChanceDePesado}");
				Checa("...`Disciplina = 0` (o `legendary_berserk_loop` nao tem UM ramo de guarda)",
					  furia.Disciplina == 0, $"{furia.Disciplina}");
				Checa("...e `Inteligencia = 0` -- e aqui isto importa MAIS que no macaco: o corpo em "
					+ "furia e de um Saiyajin com escada aberta, e sem a linha ele viraria Legendary de "
					+ "novo sozinho no meio da posse",
					  furia.Inteligencia == 0, $"{furia.Inteligencia}");

				// AS DUAS SAO DIFERENTES, e a diferenca e a do DM. Se alguem copiar uma na outra "pra
				// simplificar", isto reprova -- o macaco e pesado, a furia e um corpo em velocidade normal.
				Checa("as duas posses NAO sao a mesma coisa (relogio e peso do golpe diferem, como no DM)",
					  fera.IntervaloDeDecisao != furia.IntervaloDeDecisao
					  && fera.ChanceDePesado != furia.ChanceDePesado);

				// ---- E O COMPORTAMENTO, com os cerebros DE PRODUCAO na pior situacao possivel ----
				// E a mesma varredura da secao 10, agora sem eu ter temperado nada.
				foreach ((string posse, Cerebro cerebro) in new[] { ("fera", fera), ("furia", furia) })
				{
					cerebro.Poderes = new Capacidades
					{ TemComQueAparar = true, SabeReunirKi = true, PodeVoar = true, HaDegrauAcima = true };
					var rng = new Random(1919);
					int guardou = 0, carregou = 0, voou = 0, forma = 0;
					for (int i = 0; i < 5000; i++)
					{
						var p = new Percepcao
						{
							TemAlvo = true, Minha = Vec2.Zero,
							DoAlvo = new Vec2((float)(rng.NextDouble() * 200 - 100), 0),
							AltitudeDoAlvo = (float)(rng.NextDouble() * Voo.AlturaMaxima),
							AlvoVoando = rng.Next(2) == 0,
							VidaFrac = rng.NextDouble() * 0.4,
							KiFrac = rng.NextDouble() * 0.1,
							Ki = rng.NextDouble() * 10,
							FolegoFrac = rng.NextDouble() * 0.1,
							Atordoado = rng.Next(2) == 0,
							MeuPoder = 1000, PoderDoAlvo = 100000,
						};
						Comando c = cerebro.Pensar(p, Protocol.TickSeconds, rng);
						if (c.Guardar) guardou++;
						if (c.Carregar) carregou++;
						if (c.AlternarVoo) voou++;
						if (c.SubirForma) forma++;
					}
					Checa($"o cerebro de PRODUCAO da {posse} nao apara, nao recarrega, nao usa altura e nao "
						+ "sobe de forma -- 5000 tiques da pior situacao possivel",
						  guardou == 0 && carregou == 0 && voou == 0 && forma == 0,
						  $"guarda={guardou} carga={carregou} voo={voou} forma={forma}");
				}

				// ---- E ELAS CONTINUAM ANDANDO SEM PRESA (a regressao que a `--formasteste` pegou) ----
				var rngVaga = new Random(77);
				int andou = 0;
				for (int i = 0; i < 300; i++)
				{
					var p = new Percepcao
					{
						TemAlvo = false, Minha = Vec2.Zero, DoAlvo = new Vec2(200, 0),
						VidaFrac = 1, KiFrac = 1, Ki = 500, FolegoFrac = 1,
					};
					if (fera.Pensar(p, Protocol.TickSeconds, rngVaga).Rumo.LengthSquared > 1e-6f) andou++;
				}
				Checa("...e a fera de producao continua VAGANDO sem presa (o `Plano.Vagar`)",
					  andou > 250, $"{andou} de 300");

				DevolverAsRedeas(macaco);
				DevolverAsRedeas(lendario);
				Checa("(faxina) as redeas voltam pelo caminho de producao",
					  macaco.Cerebro == null && lendario.Cerebro == null);
			}

			// =====================================================================
			// 20. NINGUEM SOCA DE COSTAS
			// =====================================================================
			// ============================ O QUE ESTA SECAO MEDE, E POR QUE NAO E "A IA VIRA" ============================
			// O dono viu o boneco *"SOCAR DE COSTAS"*, e o defeito nao era feio: era **sem efeito**. O
			// golpe do jogo so acha alvo dentro do cone da frente (`AlvoNaFrente` -> `MeleeArea.NoAlcance`,
			// o `compileRangeMobList` do DM). De costas, a IA nao errava o soco -- ela nao tinha alvo.
			//
			// Entao a checagem que vale nao e "o `Facing` mudou": e **o soco encontrou gente**. Uma
			// bancada que so olhasse a direcao ficaria verde no dia em que alguem trocasse o cone e
			// mediria a minha propria conta contra ela mesma -- e essa e a forma exata do cego que este
			// projeto ja documentou ("as duas telas concordam" fica verde com as duas erradas igual).
			//
			// A ARMADILHA E ARMADA DE PROPOSITO: o alvo e posto ATRAS (`Facing` cravado no sentido
			// oposto) e a distancia escolhida DENTRO da faixa em que a `Pressao` manda passo pra tras
			// (`Distancia < DistanciaIdeal * 0,6`) e o soco vale ao mesmo tempo (`<= DistanciaIdeal *
			// 1,6`). E a janela em que as duas ordens se sobrepunham -- a que produzia os 180° medidos.
			// =====================================================================================================
			{
				foreach (ServerPlayer velho in _players.Values) velho.Cerebro = null;

				var origem = new Vec2(0, 13400);
				ServerPlayer soqueiro = Forjar("ia: de costas", 50_000, origem, comCerebro: false);
				// ATRAS DELE, e colado: 18 px < 34 * 0,6 = 20,4 (a faixa do passo pra tras) e
				// < 34 * 1,6 = 54,4 (a faixa do soco). Alvo sem cerebro: quem e medido e um so.
				ServerPlayer vitima = Forjar("ia: alvo nas costas", 50_000, origem - new Vec2(18, 0), comCerebro: false);

				soqueiro.Cerebro = new Cerebro();
				soqueiro.Facing = Facing.East;    // olhando pro lado OPOSTO ao alvo (que esta a oeste)
				vitima.Facing = Facing.East;

				Checa("(preparo) o alvo nasce nas COSTAS do soqueiro, no alcance e FORA do cone",
					  !MeleeArea.NoAlcance(soqueiro.Pos, soqueiro.Facing, vitima.Pos)
					  && (vitima.Pos - soqueiro.Pos).Length <= CombatKnobs.Alcance,
					  $"olhando {soqueiro.Facing}, {(vitima.Pos - soqueiro.Pos).Length:0} px");

				// UM TIQUE SO ja tem que virar o corpo: o olhar e reflexo, nao decisao -- ele sai do
				// `Montar`, que roda em TODO tique, e nao do `Repensar`, que roda a 4 Hz.
				TickDosCorposSemDono(Protocol.TickSeconds);
				Checa("UM tique dirigido e o corpo ja encara o alvo -- mesmo antes de decidir o plano",
					  soqueiro.Facing == Facing.West, $"olhando {soqueiro.Facing}");

				// ---- E O SOCO ACHA GENTE, que e a afirmacao de verdade ----
				// Mais tiques: o compromisso solta, o plano vira `Pressionar` e a receita passa a mandar
				// passo pra tras (colado demais) JUNTO com o golpe -- a combinacao que produzia o defeito.
				//
				// `cegarOOlhar` REPRODUZ O DEFEITO sem tocar em producao: com `Olhar` zerado o atuador cai
				// no rumo do passo, que e exatamente o que ele fazia antes desta camada. E o que prova
				// que a armadilha esta ARMADA -- uma checagem que nao sabe ficar vermelha nao mede nada,
				// e este projeto ja viu 4000 verdes com quatro defeitos visuais atravessando.
				(int noAr, int comAlvo, int deCostas) Medir(bool cegarOOlhar)
				{
					int socosNoAr = 0, socosComAlvo = 0, deCostas = 0;
					soqueiro.Facing = Facing.East;
					for (int i = 0; i < 240; i++)
					{
						// o alvo e RECOLADO todo tique: o que se mede e a direcao, e nao a briga de
						// posicao. Sem isto o passo pra tras afasta os dois e a armadilha se desarma.
						vitima.Pos = soqueiro.Pos - new Vec2(18, 0);
						vitima.Ficha.HP = 100; vitima.Ficha.KO = false;
						soqueiro.Combate.Recarga = 0;

						Comando c = soqueiro.Cerebro!.Pensar(
							LerPercepcao(soqueiro, vitima, vitima.Pos, quemAtira: false),
							Protocol.TickSeconds, _rng);
						if (cegarOOlhar) c = c with { Olhar = Vec2.Zero };
						AplicarComando(soqueiro, c, Protocol.TickSeconds);

						if (!c.Leve && !c.Pesado) continue;
						if (soqueiro.Facing != Facing.West) deCostas++;
						if (AlvoNaFrente(soqueiro) == null) socosNoAr++; else socosComAlvo++;
					}
					return (socosNoAr, socosComAlvo, deCostas);
				}

				(int noAr, int comAlvo, int deCostas) cego = Medir(cegarOOlhar: true);
				Checa("A ARMADILHA ESTA ARMADA: com o `Olhar` zerado -- o comportamento de antes desta "
					+ "camada -- o corpo soca de costas e o golpe sai SEM ALVO",
					  cego.deCostas > 0 && cego.noAr > 0,
					  $"de costas={cego.deCostas} no ar={cego.noAr} com alvo={cego.comAlvo}");

				(int noAr, int comAlvo, int deCostas) real = Medir(cegarOOlhar: false);
				Checa("a receita REALMENTE soca nesta janela (senao o verde de baixo seria de graca)",
					  real.comAlvo + real.noAr >= 5, $"{real.comAlvo + real.noAr} golpes em 240 tiques");
				Checa("NENHUM golpe sai com o corpo virado pro lado errado",
					  real.deCostas == 0, $"{real.deCostas} de {real.comAlvo + real.noAr}");
				Checa("...e NENHUM golpe sai sem alvo, com o oponente colado atras -- eram 38% antes",
					  real.noAr == 0, $"{real.noAr} no ar de {real.comAlvo + real.noAr}");

				// ---- QUEM VAGA OLHA PRA ONDE VAI, e nao pra um alvo que nao existe ----
				// A outra metade da regra: `Olhar` zero cai no rumo do passo. Sem esta checagem, "encarar
				// sempre" passaria despercebido e o bicho que passeia ficaria andando de lado pra sempre.
				var rngVaga2 = new Random(4242);
				int olharVazio = 0;
				for (int i = 0; i < 60; i++)
				{
					var p = new Percepcao
					{
						TemAlvo = false, Minha = Vec2.Zero, DoAlvo = new Vec2(200, 0),
						VidaFrac = 1, KiFrac = 1, Ki = 500, FolegoFrac = 1,
					};
					if (soqueiro.Cerebro!.Pensar(p, Protocol.TickSeconds, rngVaga2).Olhar.LengthSquared <= 1e-6f)
						olharVazio++;
				}
				Checa("sem alvo, o cerebro nao manda olhar pra lugar nenhum (o passo e que decide)",
					  olharVazio == 60, $"{olharVazio} de 60");

				// ---- E O CAIDO NAO GIRA ----
				// Duas travas independentes o protegem (o cerebro devolve `Comando.Nenhum` e o
				// `PodeMexerOCorpo` barra o atuador). Aqui mede-se o resultado das duas: o corpo no chao
				// mantem a direcao em que caiu, que e a que o sprite deitado desenha.
				soqueiro.Facing = Facing.North;
				soqueiro.Ficha.KO = true;
				for (int i = 0; i < 30; i++)
				{
					vitima.Pos = soqueiro.Pos - new Vec2(18, 0);
					AplicarComando(soqueiro, soqueiro.Cerebro!.Pensar(
						LerPercepcao(soqueiro, vitima, vitima.Pos, quemAtira: false),
						Protocol.TickSeconds, _rng), Protocol.TickSeconds);
				}
				Checa("corpo NOCAUTEADO nao vira pra encarar ninguem -- ele fica como caiu",
					  soqueiro.Facing == Facing.North, $"olhando {soqueiro.Facing}");
				soqueiro.Ficha.KO = false;

				// ---- E O CORPO LARGADO CONTINUA SENDO UM BONECO ----
				// Ele nao e barrado por um `if`: ele nasce sem cerebro, entao a volta dos dirigidos nao
				// o enxerga. A checagem afirma o MECANISMO, porque e ele que continua valendo quando
				// alguem escrever a proxima posse.
				ServerPlayer boneco = Forjar("ia: boneco largado", 50_000, origem + new Vec2(0, 120), comCerebro: false);
				boneco.Facing = Facing.North;
				vitima.Pos = boneco.Pos - new Vec2(18, 0);
				for (int i = 0; i < 30; i++) TickDosCorposSemDono(Protocol.TickSeconds);
				Checa("corpo LARGADO (sem cerebro) nao entra na volta e nao gira",
					  boneco.Cerebro == null && boneco.Facing == Facing.North, $"olhando {boneco.Facing}");

				soqueiro.Cerebro = null;
			}

			// =====================================================================
			// 21. O FUNDO: EMOCAO, FUGA, FINALIZADOR, RAJADA E ECONOMIA
			// =====================================================================
			// ============================ O QUE ESTA SECAO PRECISA PROVAR ============================
			// O dono disse que a IA *"ta mt simples"*. A medicao explicou por que: dos oito planos, um
			// so era alcancado numa luta de verdade, e o TEMPERO de cada corpo era uma constante --
			// o mesmo peso de golpe no primeiro soco e no ultimo, o mesmo limiar de recuo com a vida
			// cheia e no fio, e o mesmo ritmo de soco pra sempre.
			//
			// Entao as checagens daqui sao todas da mesma familia: **o mesmo corpo, em duas
			// situacoes, se comporta diferente**. Uma bancada que so afirmasse "existe o plano
			// Fugir" ficaria verde com um plano que nunca e escolhido -- que e exatamente o defeito
			// que a fase 0 mediu (sete dos oito planos existiam e nao aconteciam).
			//
			// E cada par tem o seu CONTROLE, pela mesma razao do `cegarOOlhar` da secao 20: sem o
			// lado que NAO dispara, o lado que dispara pode estar disparando sempre.
			// ====================================================================================
			{
				/// uma cena de duelo pura -- sem corpo, sem mundo: o que se mede e a decisao
				static Percepcao Duelo(double vida = 1, double vidaAlvo = 1, double razao = 1,
									   double folego = 1, double kiFrac = 1, double ki = 5000,
									   float px = 18, bool atordoado = false) => new()
				{
					TemAlvo = true, IdDoAlvo = 91, Minha = Vec2.Zero, DoAlvo = new Vec2(px, 0),
					VidaFrac = vida, VidaDoAlvo = vidaAlvo, FolegoFrac = folego,
					KiFrac = kiFrac, Ki = ki, Atordoado = atordoado,
					MeuPoder = 1000, PoderDoAlvo = 1000 * razao,
				};

				// ---------- (a) AS EMOCOES ANDAM DURANTE A LUTA ----------
				// O `behavior_check` (`NPCAI.dm:769`) em duas leituras opostas: apanhando de alguem
				// mais forte, e batendo em alguem mais fraco.
				{
					var apanhando = new Cerebro();
					var rng = new Random(21);
					double vida = 1;
					for (int i = 0; i < 900; i++)   // 30 s
					{
						vida = Math.Max(0.30, vida - 0.0008);   // ~2,4 HP por leitura de emocao
						apanhando.Pensar(Duelo(vida: vida, razao: 2.0), Protocol.TickSeconds, rng);
					}
					Checa("apanhando de alguem mais forte, a FURIA sobe acima da do molde "
						+ "(o `behavior_check`, que era constante)",
						  apanhando.FuriaExpressa > 0.35 + 0.05,
						  $"furia {apanhando.FuriaExpressa:0.00} vs base 0,35");
					Checa("...e a CAUTELA sobe junto -- e a metade do `behavior_check` que o clamp do "
						+ "DM apaga (la a coragem so pode subir)",
						  apanhando.CautelaExpressa > 0.45 + 0.05,
						  $"cautela {apanhando.CautelaExpressa:0.00} vs base 0,45");

					var ganhando = new Cerebro();
					var rng2 = new Random(22);
					for (int i = 0; i < 900; i++)
						ganhando.Pensar(Duelo(vida: 1, razao: 0.4), Protocol.TickSeconds, rng2);
					Checa("...e GANHANDO ele fica afoito: a cautela cai abaixo da do molde",
						  ganhando.CautelaExpressa < 0.45 - 0.05,
						  $"cautela {ganhando.CautelaExpressa:0.00} vs base 0,45");

					// O CONTROLE: sem alvo as emocoes zeram (o `resetState`). Sem isto o cidadao
					// carregaria a raiva da briga de ontem pro passeio de hoje.
					var semAlvo = new Percepcao { TemAlvo = false, VidaFrac = 1, KiFrac = 1, FolegoFrac = 1 };
					for (int i = 0; i < 120; i++) apanhando.Pensar(semAlvo, Protocol.TickSeconds, rng);
					Checa("...e sem alvo elas ZERAM de volta pro tempero do molde (o `resetState`)",
						  Math.Abs(apanhando.FuriaExpressa - 0.35) < 1e-9,
						  $"furia {apanhando.FuriaExpressa:0.00}");

					// E A FERA CONTINUA SENDO A FERA: `VidaCautelosa = 0` nao tem como cair, e a
					// emocao nao pode INVENTAR cautela num corpo que nao tem nenhuma.
					var fera = new Cerebro { VidaCautelosa = 0, Inteligencia = 0, Disciplina = 0 };
					var rng3 = new Random(23);
					double v = 1;
					for (int i = 0; i < 900; i++)
					{
						v = Math.Max(0.2, v - 0.001);
						fera.Pensar(Duelo(vida: v, razao: 3), Protocol.TickSeconds, rng3);
					}
					Checa("...e a fera continua com cautela ZERO depois de 30 s apanhando "
						+ "(emocao nao inventa medo em quem nao tem)",
						  fera.CautelaExpressa == 0, $"{fera.CautelaExpressa:0.00}");
				}

				// ---------- (b) FUGIR DE VERDADE (o `runawayState`, NPCAI.dm:646) ----------
				{
					/// roda ate o plano virar `Fugir`, devolvendo o comando do momento
					static (bool fugiu, Comando c) AteFugir(Cerebro cb, double vida, int tiques = 600)
					{
						var rng = new Random(31);
						Comando ult = default;
						for (int i = 0; i < tiques; i++)
						{
							ult = cb.Pensar(Duelo(vida: vida), Protocol.TickSeconds, rng);
							if (cb.Atual == Plano.Fugir) return (true, ult);
						}
						return (false, ult);
					}

					// coragem 0 -> cautela 0,9 (> 0,585): este foge
					var covarde = new Cerebro { VidaCautelosa = 0.9 };
					(bool fugiu, Comando c) f = AteFugir(covarde, vida: 0.20);
					Checa("com a vida no fio e sem coragem, ele FOGE (o plano que nunca existiu neste port)",
						  f.fugiu);
					Checa("...e a fuga e CORRENDO e pra LONGE do alvo (nao e o passo atras do recuo)",
						  f.c.Correndo && f.c.Rumo.X < -0.1f, $"correndo={f.c.Correndo} rumo={f.c.Rumo.X:0.00}");
					Checa("...e quem foge NAO ataca e NAO guarda (o laco do DM nao tem essas linhas)",
						  !f.c.Leve && !f.c.Pesado && !f.c.Guardar);
					Checa("...e ele OLHA PRA ONDE CORRE -- a terceira excecao do olhar, prevista quando "
						+ "o campo nasceu",
						  f.c.Olhar.LengthSquared <= 1e-6f, $"olhar {f.c.Olhar.X:0.00}");

					// OS CONTROLES: coragem alta nao foge, vida cheia nao foge, e a fera nunca foge.
					Checa("...mas com a MESMA vida no fio, o corajoso continua brigando (o portao e duplo)",
						  !AteFugir(new Cerebro { VidaCautelosa = 0.3 }, vida: 0.20).fugiu);
					Checa("...e com a vida cheia nem o covarde foge",
						  !AteFugir(new Cerebro { VidaCautelosa = 0.9 }, vida: 1.0).fugiu);
					Checa("...e a fera (`VidaCautelosa = 0`) nao foge nem no fio da vida",
						  !AteFugir(new Cerebro { VidaCautelosa = 0, Inteligencia = 0, Disciplina = 0 }, vida: 0.05).fugiu);

					// E ELE VOLTA: `if(src.HP > 25) chaseState` (`NPCAI.dm:650`).
					var voltou = new Cerebro { VidaCautelosa = 0.9 };
					AteFugir(voltou, vida: 0.20);
					var rngV = new Random(32);
					bool saiu = false;
					for (int i = 0; i < 300 && !saiu; i++)
					{
						voltou.Pensar(Duelo(vida: 0.9), Protocol.TickSeconds, rngV);
						saiu = voltou.Atual != Plano.Fugir;
					}
					Checa("...e com a vida de volta ele PARA de fugir (a fuga nao e um estado grudado)",
						  saiu, $"plano {voltou.Atual}");
				}

				// ---------- (c) O FINALIZADOR (`NPCAI.dm:388`) ----------
				// O consumidor da `Percepcao.VidaDoAlvo`, que era o setimo dado extraido sem leitor.
				{
					static int Socos(Cerebro cb, double vidaAlvo, int semente)
					{
						var rng = new Random(semente);
						int n = 0;
						for (int i = 0; i < 900; i++)   // 30 s
						{
							Comando c = cb.Pensar(Duelo(vidaAlvo: vidaAlvo), Protocol.TickSeconds, rng);
							if (c.Leve || c.Pesado) n++;
						}
						return n;
					}

					int fechando = Socos(new Cerebro { Agressividade = 1 }, vidaAlvo: 0.20, 41);
					int medindo = Socos(new Cerebro { Agressividade = 0 }, vidaAlvo: 0.20, 41);
					Checa("com o alvo quase caido, o AGRESSIVO fecha a luta -- ele soca bem mais que o "
						+ "mesmo corpo sem agressividade",
						  fechando > medindo * 1.2, $"{fechando} socos contra {medindo} em 30 s");

					int alvoInteiro = Socos(new Cerebro { Agressividade = 1 }, vidaAlvo: 1.0, 41);
					Checa("...e o MESMO corpo agressivo nao acelera contra um alvo inteiro (e a vida "
						+ "DELE que liga o modo, e nao a agressividade sozinha)",
						  fechando > alvoInteiro * 1.2, $"{fechando} contra {alvoInteiro}");
				}

				// ---------- (d) A RAJADA CONTADA (`BarrageAttack`, NPCAI.dm:412) ----------
				{
					static int SocosCom(Cerebro cb, double folego, int semente)
					{
						var rng = new Random(semente);
						int n = 0;
						for (int i = 0; i < 900; i++)
							if (cb.Pensar(Duelo(folego: folego), Protocol.TickSeconds, rng) is { } c
								&& (c.Leve || c.Pesado)) n++;
						return n;
					}

					// furia 0,9 (>= 0,325) e folego cheio: rajadas de 2 a 4
					int furioso = SocosCom(new Cerebro { ChanceDePesado = 0.9, Inteligencia = 1 }, 1.0, 51);
					// furia 0,1 (< 0,325): golpe avulso, sempre
					int calmo = SocosCom(new Cerebro { ChanceDePesado = 0.1, Inteligencia = 1 }, 1.0, 51);
					Checa("o FURIOSO encaixa rajadas e o CALMO da golpes avulsos -- dois ritmos que se "
						+ "reconhecem na tela",
						  furioso > calmo * 1.3, $"{furioso} socos contra {calmo} em 30 s");

					int cansado = SocosCom(new Cerebro { ChanceDePesado = 0.9, Inteligencia = 1 }, 0.10, 51);
					Checa("...e o mesmo furioso SEM FOLEGO volta pro golpe avulso (o esperto poupa a "
						+ "estamina; o burro nao)",
						  cansado < furioso * 0.8, $"{cansado} contra {furioso}");
				}

				// ---------- (e) ECONOMIA DE KI (`NPCAI.dm:404`) ----------
				{
					var tiro = new Tiro
					{
						Id = "bancada_economia", AlcanceMin = 4 * ZoneCollision.TileSize,
						AlcanceMax = 12 * ZoneCollision.TileSize, TempoDeConjuracao = 0.2,
						CustoDeKi = 100, PrecisaDeLinhaLivre = false, Precisao = 0.95,
					};
					static bool Atirou(Cerebro cb, Percepcao p)
					{
						var rng = new Random(61);
						for (int i = 0; i < 400; i++)
							if (cb.Pensar(p, Protocol.TickSeconds, rng).Habilidade != null) return true;
						return false;
					}
					Cerebro Armado(double intel) => new()
					{
						Inteligencia = intel, Disciplina = 0,
						Poderes = new Capacidades { DeLonge = new Arsenal([tiro]) },
					};

					Percepcao noFundo = Duelo(kiFrac: 0.20, ki: 5000, px: 8 * ZoneCollision.TileSize);
					Percepcao cheio = Duelo(kiFrac: 1.0, ki: 5000, px: 8 * ZoneCollision.TileSize);

					Checa("com o tanque em 20%, o ESPERTO guarda o Ki e vai de soco (o `NPC_AI_KI_LOW`)",
						  !Atirou(Armado(1.0), noFundo));
					Checa("...e o BURRO queima o que sobrou no raio (erro caracteristico, nao aleatorio)",
						  Atirou(Armado(0.0), noFundo));
					Checa("...e o mesmo esperto atira com o tanque cheio (era economia, e nao timidez)",
						  Atirou(Armado(1.0), cheio));
				}

				// ---------- (f) GUARDA QUE O TANQUE NAO PAGA ----------
				// O `MeleeResolver` derruba a guarda de quem nao tem Ki pro golpe aparado
				// (`MeleeResolver.cs:139`); insistir nela e ficar com o gesto erguido levando o soco.
				{
					static bool Guardou(Cerebro cb, double ki)
					{
						var rng = new Random(71);
						for (int i = 0; i < 600; i++)
							if (cb.Pensar(Duelo(vida: 0.20, ki: ki, atordoado: true),
										  Protocol.TickSeconds, rng).Guardar) return true;
						return false;
					}
					Cerebro Disciplinado() => new()
					{
						Disciplina = 1, Inteligencia = 1,
						Poderes = new Capacidades { TemComQueAparar = true, CustoDaGuarda = 500 },
					};
					Checa("sem Ki pra pagar UM golpe aparado, ele nao ergue a guarda (o resolvedor a "
						+ "derrubaria no primeiro soco)",
						  !Guardou(Disciplinado(), ki: 100));
					Checa("...e com Ki pra pagar ele ergue (era o preco, e nao outra recusa)",
						  Guardou(Disciplinado(), ki: 5000));
				}

				// ---------- (g) A DEFENSIVA POR CANSACO (`NPCAI.dm:525`) ----------
				// Vida cheia, sem atordoamento e sem ritmo conhecido: antes desta camada NADA fazia
				// este corpo erguer a guarda. O folego no chao faz.
				{
					static bool GuardouPorCansaco(double folego)
					{
						var cb = new Cerebro
						{
							Disciplina = 1, Inteligencia = 1,
							Poderes = new Capacidades { TemComQueAparar = true, CustoDaGuarda = 1 },
						};
						var rng = new Random(81);
						for (int i = 0; i < 900; i++)
							if (cb.Pensar(Duelo(vida: 1, folego: folego, ki: 5000),
										  Protocol.TickSeconds, rng).Guardar) return true;
						return false;
					}
					Checa("com o folego no chao e o alvo colado, ele passa a APARAR mesmo inteiro "
						+ "(a postura defensiva do DM)",
						  GuardouPorCansaco(0.10));
					Checa("...e com folego cheio, inteiro e sem ritmo conhecido, ele NAO guarda "
						+ "(o controle: era o cansaco)",
						  !GuardouPorCansaco(1.0));
				}

				// ---------- (h) A DECOLAGEM QUE O TANQUE PAGA ----------
				{
					static bool PediuVoo(double ki)
					{
						var cb = new Cerebro
						{
							Inteligencia = 1,
							Poderes = new Capacidades
							{
								PodeVoar = true, CustoDeDecolar = 200, CustoDoVooPorSegundo = 100,
							},
						};
						var rng = new Random(91);
						for (int i = 0; i < 400; i++)
						{
							// alvo DOIS andares acima: a unica razao de subir que nao depende de gosto
							var p = new Percepcao
							{
								TemAlvo = true, IdDoAlvo = 5, Minha = Vec2.Zero, DoAlvo = new Vec2(64, 0),
								AltitudeDoAlvo = Voo.AlturaMaxima * 0.5f, AlvoVoando = true,
								VidaFrac = 1, KiFrac = 0.5, Ki = ki, FolegoFrac = 1,
								MeuPoder = 1000, PoderDoAlvo = 1000,
							};
							if (cb.Pensar(p, Protocol.TickSeconds, rng).AlternarVoo) return true;
						}
						return false;
					}
					Checa("com Ki pra decolar mas nao pra ficar no ar, ele NAO sobe (os dois custos "
						+ "vinham das capacidades e ninguem os lia)",
						  !PediuVoo(300), "500 = 200 de decolagem + 3 s a 100");
					Checa("...e com o tanque cobrindo a subida ele sobe",
						  PediuVoo(900));
				}

				// ---------- (i) DOIS DO MESMO MOLDE NAO SAO O MESMO BICHO ----------
				{
					MoldeDeNpc molde = _moldes?.Get("cidadao") ?? new MoldeDeNpc();
					var cautelas = new HashSet<double>();
					var pesos = new HashSet<double>();
					for (ulong s = 1; s <= 40; s++)
					{
						Cerebro c = Temperamento.Montar(molde, 0, s);
						cautelas.Add(Math.Round(c.VidaCautelosa, 4));
						pesos.Add(Math.Round(c.ChanceDePesado, 4));
					}
					Checa("40 cidadaos do MESMO molde nascem com temperos diferentes (o `rand(8,13)/10` "
						+ "do `bhv_set`)",
						  cautelas.Count >= 3 && pesos.Count >= 3,
						  $"{cautelas.Count} cautelas e {pesos.Count} pesos distintos");

					Checa("...e o mesmo corpo nasce sempre igual (o sorteio e da SEMENTE, e nao do relogio)",
						  Temperamento.Montar(molde, 0, 7).VidaCautelosa
						  == Temperamento.Montar(molde, 0, 7).VidaCautelosa);

					Checa("...e semente ZERO devolve os numeros do arquivo, sem tempero por cima "
						+ "(e o que deixa uma bancada medir a receita, e nao o sorteio)",
						  Math.Abs(Temperamento.Montar(molde, 0).VidaCautelosa
								   - 0.9 * (1 - molde.Coragem / 100.0)) < 1e-9);
				}

				// ---------- (j) A DISTANCIA DE AGRESSAO E O LEASH ----------
				// Aqui o corpo forjado nao serve: `PresaDoHostil` so olha GENTE DE VERDADE
				// (`EhJogador`), e quem tem `Peer` nesta bancada e o proprio robo que a disparou.
				{
					ServerPlayer cacador = Forjar("ia: hostil", 50_000, quem.Pos + new Vec2(40 * ZoneCollision.TileSize, 0), comCerebro: false);
					ZoneList(cacador.Zone.Hash).Remove(cacador);
					cacador.Zone = quem.Zone;
					ZoneList(cacador.Zone.Hash).Add(cacador);

					Checa("(preparo) o robo desta bancada conta como gente pra caca",
						  EhJogador(quem) && !quem.Ficha.dead && !quem.Ficha.KO);

					Checa("a 40 tiles, o hostil NAO ve ninguem -- ele cacava a zona inteira antes",
						  PresaDoHostil(cacador) == null);

					cacador.Pos = quem.Pos + new Vec2(10 * ZoneCollision.TileSize, 0);
					Checa("...a 10 tiles ele NOTA (o `MAX_AGGRO_RANGE` de 20)",
						  PresaDoHostil(cacador)?.Id == quem.Id);

					cacador.Pos = quem.Pos + new Vec2(40 * ZoneCollision.TileSize, 0);
					Checa("...e depois de engajado ele SEGUE a 40 tiles -- o raio de largar e maior que "
						+ "o de adotar, e a folga entre os dois e o que impede a briga de piscar",
						  PresaDoHostil(cacador)?.Id == quem.Id);

					cacador.Pos = quem.Pos + new Vec2(70 * ZoneCollision.TileSize, 0);
					Checa("...e alem de 60 tiles ele LARGA (o leash do `aggro_dist*2`)",
						  PresaDoHostil(cacador) == null && cacador.PresaEngajada == 0);
				}
			}
		}
		finally
		{
			foreach (ServerPlayer n in nascidos)
				if (_players.ContainsKey(n.Id)) RemoverNpc(n);

			GD.Print($"===== FIM: {ok} ok, {falhou} falha(s) =====\n");
			if (falhou > 0) GD.PushError($"[server] bancada da IA: {falhou} falha(s)");
			Avisar(quem, $"bancada da IA: {ok} ok, {falhou} falha(s) -- veja o console.");
		}
	}

	// =========================================================================
	// AS FERRAMENTAS DA VARREDURA DE FONTES (secoes 15 e 18)
	// =========================================================================
	// ============================ POR QUE UMA BANCADA LE CODIGO-FONTE ============================
	// Porque as duas afirmacoes destas secoes sao sobre CONJUNTO e sobre QUEM CHAMA -- "o atuador nao
	// chama nada fora do funil", "o `catch` despeja o nome do corpo". Nenhuma das duas aparece num
	// campo, num tique ou numa tela: um teste de comportamento so ve o gesto que EXISTE hoje, e o
	// proximo atalho e por definicao o que ainda nao foi escrito.
	//
	// Sao quatro funcoes pequenas e todas puras (recebem linhas, devolvem conjunto), de proposito: e o
	// que permite roda-las sobre uma COPIA ADULTERADA do fonte e provar que elas reprovam -- que e a
	// unica coisa que separa uma varredura de um comentario bonito.
	// ========================================================================================

	/// <summary>Le um fonte do projeto. Devolve vazio se sumir -- e a checagem que chamou reprova com o caminho.</summary>
	private static string[] Fonte(string relativo)
	{
		string caminho = Godot.ProjectSettings.GlobalizePath("res://" + relativo);
		return System.IO.File.Exists(caminho) ? System.IO.File.ReadAllLines(caminho) : [];
	}

	/// <summary>
	/// TIRA COMENTARIO, TEXTO E LITERAL DE CARACTERE de uma linha.
	///
	/// As tres coisas mentem pras contas de baixo pelo mesmo motivo: elas contem o codigo escrito
	/// POR EXTENSO. O cabecalho do proprio `GameServer.Ia.cs` lista `npc.Combate.Guardar(bool)` num
	/// comentario, e sem esta limpeza a varredura acharia chamadas e escritas que nao existem.
	/// </summary>
	private static string SemTextoNemComentario(string linha)
	{
		var limpa = new System.Text.StringBuilder(linha.Length);
		for (int i = 0; i < linha.Length; i++)
		{
			char c = linha[i];
			if (c == '/' && i + 1 < linha.Length && linha[i + 1] == '/') break;
			if (c is '"' or '\'')
			{
				char fecha = c;
				for (i++; i < linha.Length; i++)
				{
					if (linha[i] == '\\') { i++; continue; }
					if (linha[i] == fecha) break;
				}
				continue;
			}
			limpa.Append(c);
		}
		return limpa.ToString();
	}

	/// <summary>
	/// O CORPO DE UM METODO, por contagem de chaves, ja limpo de texto e comentario.
	///
	/// A assinatura e casada por `Contains` e nao por igualdade: ela vem escrita no chamador
	/// exatamente como esta no fonte (`"private void Input(NetPeer"`), e uma mudanca de assinatura
	/// devolve ZERO linhas -- que reprova alto na checagem "o corpo foi extraido", em vez de devolver
	/// um conjunto vazio que passaria por "nao achei nada de errado".
	/// </summary>
	private static string[] CorpoDoMetodo(string[] linhas, string assinatura)
	{
		int i = Array.FindIndex(linhas, l => l.Contains(assinatura));
		if (i < 0) return [];

		var saida = new List<string>();
		int nivel = 0;
		bool abriu = false;
		for (; i < linhas.Length; i++)
		{
			string l = SemTextoNemComentario(linhas[i]);
			foreach (char c in l)
			{
				if (c == '{') { nivel++; abriu = true; }
				else if (c == '}') nivel--;
			}
			if (abriu) saida.Add(l);
			if (abriu && nivel <= 0) break;
		}
		return [.. saida];
	}

	/// <summary>Palavras que levam parentese e nao sao chamada de funcao.</summary>
	private static readonly string[] NaoSaoChamadas =
		["if", "for", "foreach", "while", "switch", "return", "catch", "using", "lock", "fixed",
		 "nameof", "typeof", "sizeof", "default", "new", "do", "else", "in", "is", "and", "or", "not"];

	/// <summary>TODA FUNCAO CHAMADA nestas linhas, pelo nome curto (`a.Combate.Guardar(x)` vira `Guardar`).</summary>
	private static HashSet<string> ChamadasDe(IEnumerable<string> linhas)
	{
		var achadas = new HashSet<string>(StringComparer.Ordinal);
		foreach (string l in linhas)
			foreach (System.Text.RegularExpressions.Match m in
					 System.Text.RegularExpressions.Regex.Matches(l, @"([A-Za-z_][A-Za-z0-9_]*)\s*\("))
				if (Array.IndexOf(NaoSaoChamadas, m.Groups[1].Value) < 0) achadas.Add(m.Groups[1].Value);
		return achadas;
	}

	/// <summary>
	/// TODO CAMPO DE CORPO ESCRITO nestas linhas, pelo caminho (`npc.Ficha.dashing = x` vira `Ficha.dashing`).
	///
	/// O `[-+*/|&amp;^]?=` seguido de `(?!=)` pega atribuicao e atribuicao composta (`Ki -= 10`) e deixa
	/// passar comparacao (`==`, `&lt;=`, `&gt;=`, `!=`) -- e a subtracao disfarcada de conta e justamente a
	/// forma mais comum de um atalho de custo.
	/// </summary>
	private static HashSet<string> EscritasEm(IEnumerable<string> linhas, params string[] variaveis)
	{
		var achadas = new HashSet<string>(StringComparer.Ordinal);
		foreach (string v in variaveis)
		{
			var re = new System.Text.RegularExpressions.Regex(
				@"\b" + v + @"\.((?:[A-Za-z_][A-Za-z0-9_]*\.)*[A-Za-z_][A-Za-z0-9_]*)\s*[-+*/|&^]?=(?!=)");
			foreach (string l in linhas)
				foreach (System.Text.RegularExpressions.Match m in re.Matches(l))
					achadas.Add(m.Groups[1].Value);
		}
		return achadas;
	}
}
