using System.Text.Json;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO TRANSE (`--transeteste`) -- os itens 1 e 2 do playtest de 2026-10-10: a saida da meditacao
/// profunda, o corpo real que nao pode tomar o dano de dentro, a pose do corpo que fica, e o vigor do Majin.
///
/// No PRIMEIRO LOGIN, e hospedando: a porta da mente recusa corpo sem `Peer` ("clone nao medita"), entao quem
/// mergulha aqui e o host de verdade -- pela `EntrarNaMente` de producao, com o reflexo, o corpo largado e a
/// foto que ela monta. Nenhum tique corre no meio: cada borda e chamada pela funcao de producao dela
/// (`TickDeQuemVolta`, `MoveToZone`, o funil dos verbos).
///
///   1. O CORPO LARGADO TEM CORPO PROPRIO: o que acontece la dentro nao encosta nele, e a pose dele e a dele.
///   2. A SAIDA: o verbo do botao/da tecla, e o verb antigo -- volta como entrou, e acorda parado.
///   3. O QUE ACONTECE LA FORA E REAL: o golpe e o nocaute no corpo largado chegam ao dono.
///   4. A FAXINA: o que foi ligado la dentro nao sai; o que ja estava ligado fica.
///   5. TIRADO POR FORA (o `MoveToZone` de um admin, do torneio): o transe fecha direito.
///   6. O SAVE EM TRANSE leva o corpo de verdade, e o login o aplica.
///   7. OS ESTADOS QUE NAO DEVIAM EXISTIR ainda tem saida.
///   8. O PISO DE MEIO TANQUE do original (`StaminaDrain.dm:19-20`) nao vale pra mais ninguem (v0.3.0).
///   9. O MORTO DE PE MERGULHA (v0.3.0, item 26): o fantasma entra na mente e fica de pe la dentro; cair la dentro nao e a
///      segunda morte; o golpe letal no corpo de fora e; quem e revivido no meio do transe continua vivo; a pena do Inferno
///      nao o arranca; e o cadaver continua recusado.
///
/// Os defeitos injetados: o corpo dividido de antes (`BonecoDivideOCorpoDeTeste`), o bio com as passivas do DM
/// (`SangueDoBio.BioComOsPassivosDeAntesDeTeste`, que devolve o piso), e os tres do morto na mente -- a porta de antes
/// (`MortoNaoMergulhaDeTeste`), a borda de antes (`MortoExpulsoDaMenteDeTeste`) e a mente que deita o morto
/// (`Alem.MenteDeitaOMortoDeTeste`).
///
///     Godot --headless --path . --host --rede 7984 --transeteste --raca Human --conta bancada_transe --nome Monge
/// </summary>
public partial class GameServer
{
	private bool _transeDeTeste;

	private void RodarBancadaDoTranse(ServerPlayer pl)
	{
		GD.Print("\n[transe] ================ A MEDITACAO PROFUNDA E O CORPO QUE FICA (playtest, 2026-10-10) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[transe]   OK    {nome}"); }
			else { falhou++; GD.PrintErr($"[transe]   FALHA {nome}   [{detalhe}]"); }
		}

		// ============================ O HOST VOLTA COMO ESTAVA ============================
		ZoneKey zonaDeAntes = pl.Zone;
		Vec2 posDeAntes = pl.Pos;
		Dictionary<string, double[]> corpoDeAntes = FotografarCorpo(pl.Combate);
		double kiDeAntes = pl.Ficha.Ki, vigorDeAntes = pl.Ficha.stamina;
		bool letalDeAntes = pl.Combate.Letal, medDeAntes = pl.Ficha.med, trainDeAntes = pl.Ficha.train;
		bool koDeAntes = pl.Ficha.KO, mortoDeAntes = pl.Ficha.dead;
		// (v0.3.0) a etapa da morte, a pena e o passe -- a secao 9 faz do host um fantasma
		bool jaViajouDeAntes = pl.MorteJaViajou;
		long relogioDeAntes = pl.RelogioDaMorte, penaDeAntes = pl.Ficha.hell_lockout_until, passeDeAntes = pl.Ficha.passe_do_enma_ate;

		ZoneKey terra = ZoneKey.Premade("Earth");
		const string BuffDeAntes = "bancada_de_antes", BuffDeDentro = "bancada_de_dentro";

		BodyPart membro = pl.Combate.Corpo.Partes.First(p => p.Papel == Vitalidade.Membro);
		BodyPart outro = pl.Combate.Corpo.Partes.Last(p => p.Papel == Vitalidade.Membro);
		double feridaDeEntrada = membro.VidaMax * 0.7;
		double kiDeEntrada = 0, vigorDeEntrada = 0;
		Vec2 lugar = default;

		// O CORPO DE PARTIDA DE CADA CENA: inteiro, menos um membro a 70% -- a ferida com que ele ENTRA. E ela
		// que separa "voltou como entrou" de "voltou curado" e de "voltou com o dano de dentro".
		void Remontar()
		{
			if (NaMente(pl)) SairDoTranse(pl);
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;
			pl.Combate.NocauteRestante = 0;
			foreach (BodyPart p in pl.Combate.Corpo.Partes) { p.Decepado = false; p.Vida = p.VidaMax; }
			membro.Vida = feridaDeEntrada;
			pl.Combate.SincronizarVida();
			pl.Ficha.Ki = kiDeEntrada = Math.Round(pl.Ficha.MaxKi * 0.8);
			pl.Ficha.stamina = vigorDeEntrada = Math.Round(pl.Ficha.maxstamina * 0.9);
			if (!pl.Zone.Equals(terra) || !pl.Pos.Equals(lugar)) MoveToZone(pl.Id, terra, lugar);
		}
		bool Mergulhar()
		{
			pl.Ficha.med = true;
			EntrarNaMente(pl);
			return NaMente(pl) && pl.BonecoLargado != null;
		}
		List<string> Acordar() => Ouvir(() => Verbo(pl, "mente_sair", ""));
		string Estado() =>
			$"zona={pl.Zone.Name} membro={membro.Vida:0.#} outro={outro.Vida:0.#} ki={pl.Ficha.Ki:0} vigor={pl.Ficha.stamina:0} "
			+ $"KO={pl.Ficha.KO} boneco={(pl.BonecoLargado != null)} foto={(pl.FotoDaMente != null)}";

		try
		{
			_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
			lugar = PontoDeNascimento(terra);
			Remontar();

			// =====================================================================
			// 1) O CORPO LARGADO TEM CORPO PROPRIO
			// =====================================================================
			GD.Print("[transe] -- 1) o corpo largado tem corpo proprio --");
			bool entrou = Mergulhar();
			ServerPlayer? boneco = pl.BonecoLargado;
			Checa("(montagem) o host mergulhou de verdade: esta numa mente, e deixou um corpo la fora",
				  entrou && boneco != null && ZoneList(terra.Hash).Contains(boneco!), Estado());
			if (boneco == null) return;

			Checa("**O CORPO QUE FICA TEM FICHA E CORPO SO DELE** (nao sao os mesmos objetos do dono), iguais aos da entrada",
				  TemCorpoProprio(boneco, pl) && !ReferenceEquals(boneco.Ficha, pl.Ficha)
				  && boneco.Combate.Corpo.Achar(membro.Nome)?.Vida == feridaDeEntrada
				  && boneco.Ficha.Ki == kiDeEntrada && boneco.Ficha.stamina == vigorDeEntrada,
				  $"membro do boneco={boneco.Combate.Corpo.Achar(membro.Nome)?.Vida:0.#} ki={boneco.Ficha.Ki:0}");
			Checa("...e ele esta MEDITANDO pra quem passa (a pose que viaja no snapshot)",
				  boneco.Pose(NowMs(), false) == Protocol.Pose.Meditando, $"{boneco.Pose(NowMs(), false)}");

			// O QUE ACONTECE LA DENTRO: apanha, gasta Ki, cansa.
			outro.Vida = 5;
			membro.Vida = 10;
			pl.Ficha.Ki = 1;
			pl.Ficha.stamina = 1;
			pl.Combate.SincronizarVida();
			Checa("**O DANO DE DENTRO NAO ENCOSTA NO CORPO DE FORA**, nem enquanto a luta dura: membros, Ki e vigor dele continuam os da entrada",
				  boneco.Combate.Corpo.Achar(membro.Nome)?.Vida == feridaDeEntrada
				  && boneco.Combate.Corpo.Achar(outro.Nome)?.Vida == outro.VidaMax
				  && boneco.Ficha.Ki == kiDeEntrada && boneco.Ficha.stamina == vigorDeEntrada);

			// A POSE: o primeiro passo la dentro manda "parado", e o T manda "treinando".
			DefinirAtividade(pl, Protocol.Activity.Parado);
			bool depoisDoPasso = boneco.Pose(NowMs(), false) == Protocol.Pose.Meditando && !pl.Ficha.med;
			pl.Ficha.stamina = pl.Ficha.maxstamina;   // (2026-10-24) o T exausto e recusado no pedido: descansado, o dono treina la dentro de verdade
			DefinirAtividade(pl, Protocol.Activity.Treinando);
			bool depoisDoTreino = boneco.Pose(NowMs(), false) == Protocol.Pose.Meditando && pl.Ficha.train;
			pl.Ficha.stamina = 1;   // ...e o cansaco de dentro volta, pra a saida provar que ele nao sai junto
			DefinirAtividade(pl, Protocol.Activity.Parado);
			pl.Combate.Nocautear(10);
			bool depoisDoNocaute = boneco.Pose(NowMs(), false) == Protocol.Pose.Meditando;
			pl.Ficha.KO = false;
			pl.Combate.NocauteRestante = 0;
			Checa("**A POSE DO CORPO E A DELE**: o dono anda, treina e ate cai la dentro, e o corpo la fora continua meditando",
				  depoisDoPasso && depoisDoTreino && depoisDoNocaute,
				  $"passo={depoisDoPasso} treino={depoisDoTreino} nocaute={depoisDoNocaute}");

			// =====================================================================
			// 2) A SAIDA
			// =====================================================================
			GD.Print("[transe] -- 2) a saida --");
			List<string> falas = Acordar();
			Checa("**O VERBO DA SAIDA ACORDA** (o botao do alto da tela e a tecla de meditar mandam este): fora da mente, no lugar do corpo",
				  !NaMente(pl) && pl.Zone.Equals(terra) && pl.Pos.Equals(lugar) && Disse(falas, "abre os olhos"),
				  $"{Estado()} | {Ultimos(falas)}");
			Checa("...sem corpo largado e sem foto pendentes, e nenhum corpo dele sobrando na zona",
				  pl.BonecoLargado == null && pl.FotoDaMente == null
				  && !ZoneList(terra.Hash).Any(o => o.DonoDoCorpoLargado == pl.Id), Estado());
			Checa("**O CORPO VOLTA COMO ENTROU**: a ferida de entrada (70%) esta la, o membro quebrado la dentro voltou inteiro, e o Ki e o vigor sao os da entrada",
				  membro.Vida == feridaDeEntrada && outro.Vida == outro.VidaMax
				  && pl.Ficha.Ki == kiDeEntrada && pl.Ficha.stamina == vigorDeEntrada, Estado());
			Checa("...e ele acorda PARADO (nem meditando, nem treinando)", !pl.Ficha.med && !pl.Ficha.train);

			falas = Acordar();
			Checa("fora de transe o verbo so responde que nao ha de onde sair", Disse(falas, "nao esta em transe") && pl.Zone.Equals(terra), Ultimos(falas));

			Remontar();
			Mergulhar();
			UsarHabilidade(pl, "sairdamente");
			Checa("o verb antigo do menu P (\"Sair da mente\") sai pela MESMA porta", !NaMente(pl) && pl.BonecoLargado == null, Estado());

			// =====================================================================
			// 3) O QUE ACONTECE LA FORA E REAL
			// =====================================================================
			GD.Print("[transe] -- 3) o que acontece la fora e real --");
			ServerPlayer agressor = Forjar("Agressor", lugar + new Vec2(32, 0), 1000);

			// A CENA: la dentro um membro vai a 5; la fora alguem tira 20 do membro ja ferido do corpo.
			(bool Voltou, double Membro, double Outro) GolpeDeFora()
			{
				Remontar();
				Mergulhar();
				ServerPlayer corpo = pl.BonecoLargado!;
				outro.Vida = 5;
				pl.Combate.SincronizarVida();
				BodyPart alvo = corpo.Combate.Corpo.Achar(membro.Nome)!;
				alvo.Vida -= 20;
				corpo.Combate.SincronizarVida();
				MarcarAgressao(corpo, agressor);
				TickDeQuemVolta();
				return (!NaMente(pl) && pl.BonecoLargado == null, membro.Vida, outro.Vida);
			}

			(bool voltou, double mDepois, double oDepois) = GolpeDeFora();
			Checa("**O GOLPE NO CORPO LARGADO E REAL**: o dono acorda, o membro atingido la fora volta 20 abaixo da entrada -- e o quebrado la dentro volta inteiro",
				  voltou && mDepois == feridaDeEntrada - 20 && oDepois == outro.VidaMax,
				  $"voltou={voltou} membro={mDepois:0.#} (entrada {feridaDeEntrada:0.#}) outro={oDepois:0.#}");

			// ---- A CONTRAPROVA: o corpo dividido de antes ----
			BonecoDivideOCorpoDeTeste = true;
			bool poseCaiu;
			try
			{
				(voltou, mDepois, oDepois) = GolpeDeFora();
				Remontar();
				Mergulhar();
				DefinirAtividade(pl, Protocol.Activity.Parado);
				poseCaiu = pl.BonecoLargado!.Pose(NowMs(), false) != Protocol.Pose.Meditando;
				SairDoTranse(pl);
			}
			finally { BonecoDivideOCorpoDeTeste = false; }
			Checa("(defeito injetado: o corpo largado divide o corpo do dono) o golpe de fora e APAGADO pela foto, e o primeiro passo la dentro derruba a pose do corpo -- as duas reguas sabem reprovar",
				  mDepois == feridaDeEntrada && poseCaiu, $"membro={mDepois:0.#} (com o defeito volta a {feridaDeEntrada:0.#}) poseCaiu={poseCaiu}");

			// ---- O NOCAUTE DE FORA ----
			Remontar();
			Mergulhar();
			ServerPlayer corpoCaido = pl.BonecoLargado!;
			corpoCaido.Combate.Nocautear(40);
			bool poseDoCaido = corpoCaido.Pose(NowMs(), false) == Protocol.Pose.Nocauteado;
			MarcarAgressao(corpoCaido, agressor);
			TickDeQuemVolta();
			Checa("**O NOCAUTE NO CORPO LARGADO E REAL**: ele cai la fora (quem passa ve), e o dono acorda CAIDO, pelo `Nocautear` dele, com o tempo que faltava",
				  poseDoCaido && !NaMente(pl) && pl.Ficha.KO && Math.Abs(pl.Combate.NocauteRestante - 40) < 0.5,
				  $"poseDoCaido={poseDoCaido} {Estado()} restante={pl.Combate.NocauteRestante:0.#}");

			// =====================================================================
			// 4) A FAXINA
			// =====================================================================
			GD.Print("[transe] -- 4) a faxina da saida --");
			Remontar();
			pl.Combate.Letal = false;
			LigarBuff(pl, BuffDeAntes, "Buff de Antes", []);
			Mergulhar();
			LigarBuff(pl, BuffDeDentro, "Buff de Dentro", []);
			pl.TiquesDeVoo = 9;
			pl.ForcaDoVoo = 3;
			pl.Combate.EmCombate = 90;
			pl.Combate.LutandoDeVerdade = 10;
			pl.Combate.Stun = 2;
			pl.Combate.Combo = 3;
			pl.Combate.Letal = true;
			_paralisadoAte[pl.Id] = NowMs() + 60_000;
			pl.UltimoAgressor = pl.CloneId;
			pl.RancorAte = NowMs() + 90_000;
			DefinirAtividade(pl, Protocol.Activity.Treinando);
			Acordar();
			Checa("**O QUE FOI LIGADO LA DENTRO NAO SAI**: o buff de dentro caiu, e o que ja estava ligado antes de entrar continua",
				  !TemBuff(pl, BuffDeDentro) && TemBuff(pl, BuffDeAntes));
			Checa("...o arremesso em curso, a paralisia e o rancor do reflexo ficaram la",
				  pl.TiquesDeVoo == 0 && pl.ForcaDoVoo == 0 && !_paralisadoAte.ContainsKey(pl.Id)
				  && pl.UltimoAgressor == 0 && pl.RancorAte == 0,
				  $"voo={pl.TiquesDeVoo} paralisado={_paralisadoAte.ContainsKey(pl.Id)} agressor={pl.UltimoAgressor}");
			Checa("...e o relogio da luta tambem: sem marca de combate, sem atordoamento, sem combo -- e o modo LETAL voltou a ser o da entrada",
				  pl.Combate.EmCombate == 0 && pl.Combate.LutandoDeVerdade == 0 && pl.Combate.Stun == 0
				  && pl.Combate.Combo == 0 && !pl.Combate.Letal && !pl.Ficha.train,
				  $"emCombate={pl.Combate.EmCombate} stun={pl.Combate.Stun} combo={pl.Combate.Combo} letal={pl.Combate.Letal}");
			DesligarBuff(pl, BuffDeAntes);

			// =====================================================================
			// 5) TIRADO POR FORA
			// =====================================================================
			GD.Print("[transe] -- 5) tirado da mente por fora --");
			Remontar();
			Mergulhar();
			outro.Vida = 5;
			pl.Combate.SincronizarVida();
			Vec2 praOnde = lugar + new Vec2(96, 0);
			falas = Ouvir(() => MoveToZone(pl.Id, terra, praOnde));   // o que o `admin_trazer` e o torneio fazem
			Checa("**UM `MoveToZone` DE FORA FECHA O TRANSE**: o corpo volta como entrou, nada fica pendente, e ele chega ONDE FOI LEVADO",
				  !NaMente(pl) && pl.BonecoLargado == null && pl.FotoDaMente == null && outro.Vida == outro.VidaMax
				  && membro.Vida == feridaDeEntrada && pl.Pos.Equals(praOnde) && Disse(falas, "arrancado do transe"),
				  $"{Estado()} | {Ultimos(falas)}");
			Checa("...sem deixar um corpo meditando no lugar de onde saiu",
				  !ZoneList(terra.Hash).Any(o => o.DonoDoCorpoLargado == pl.Id));
			Remontar();
			Checa("...e a meditacao profunda CONTINUA funcionando depois (antes ela era recusada em silencio: \"ja esta fora do corpo\")",
				  Mergulhar(), Estado());
			SairDoTranse(pl);

			// =====================================================================
			// 6) O SAVE EM TRANSE
			// =====================================================================
			GD.Print("[transe] -- 6) o save de quem esta em transe --");
			Remontar();
			Mergulhar();
			outro.Vida = 5;
			pl.Ficha.Ki = 1;
			pl.Combate.SincronizarVida();
			CharacterSave gravado = AccountStore.DeJogador(pl, NowMs());
			Checa("**O SAVE DE QUEM ESTA NUMA MENTE LEVA O CORPO DE VERDADE**: o endereco do corpo largado e o estado dele",
				  gravado.Transe is { } t && DimensaoMental.EhAMente(new ZoneKey(gravado.ZonaTipo, gravado.Zona, gravado.ZonaSeed))
				  && t.Zona == terra.Name && t.X == lugar.X && t.Y == lugar.Y
				  && t.Membros[membro.Nome][0] == feridaDeEntrada && t.Membros[outro.Nome][0] == outro.VidaMax && t.Ki == kiDeEntrada,
				  $"transe={(gravado.Transe != null)} zona do avatar={gravado.Zona}");

			// O CAMINHO DO DISCO: serializa, le de volta e aplica -- o que o login faz antes de ler qualquer coisa.
			var opcoes = new JsonSerializerOptions { IncludeFields = true };
			CharacterSave lido = JsonSerializer.Deserialize<CharacterSave>(JsonSerializer.Serialize(gravado, opcoes), opcoes)!;
			TirarOSaveDoTranse(lido);
			Checa("...e o LOGIN acorda no corpo: a zona e o ponto sao os do corpo, os membros e o Ki sao os de verdade, e o bloco some",
				  lido.Transe == null && lido.Zona == terra.Name && lido.ZonaTipo == terra.Kind && lido.X == lugar.X && lido.Y == lugar.Y
				  && lido.Membros[membro.Nome][0] == feridaDeEntrada && lido.Membros[outro.Nome][0] == outro.VidaMax
				  && lido.Ficha.Ki == kiDeEntrada && !lido.Ficha.med,
				  $"zona={lido.Zona} membro={lido.Membros[membro.Nome][0]:0.#} outro={lido.Membros[outro.Nome][0]:0.#} ki={lido.Ficha.Ki:0}");
			SairDoTranse(pl);
			Checa("fora de transe o save NAO leva o bloco", AccountStore.DeJogador(pl, NowMs()).Transe == null);

			// =====================================================================
			// 7) OS ESTADOS QUE NAO DEVIAM EXISTIR
			// =====================================================================
			GD.Print("[transe] -- 7) os estados inconsistentes ainda tem saida --");
			Remontar();
			Mergulhar();
			ServerPlayer sumido = pl.BonecoLargado!;
			pl.BonecoLargado = null;
			sumido.DonoDoCorpoLargado = 0;
			RemoverNpc(sumido);
			pl.FotoDaMente = null;
			LigarBuff(pl, BuffDeDentro, "Buff de Dentro", []);
			falas = Acordar();
			Checa("**NUMA MENTE SEM CORPO E SEM FOTO** (o relog depois de uma queda do servidor antigo) o verbo AINDA tira o jogador de la, e a faxina roda",
				  !NaMente(pl) && !DimensaoMental.EhAMente(pl.Zone) && !TemBuff(pl, BuffDeDentro), $"{Estado()} | {Ultimos(falas)}");

			Remontar();
			Mergulhar();
			DesfazerOOponente(pl, "");
			outro.Vida = 5;
			pl.Combate.SincronizarVida();
			ZoneList(pl.Zone.Hash).Remove(pl);   // o defeito de antes, montado a mao: fora da mente com o transe pendente
			pl.Zone = terra;
			ZoneList(terra.Hash).Add(pl);
			falas = Acordar();
			Checa("**FORA DA MENTE COM UM TRANSE PENDENTE** (o que um teleporte de admin deixava) o verbo fecha o que sobrou: corpo de volta, nada pendente",
				  pl.BonecoLargado == null && pl.FotoDaMente == null && outro.Vida == outro.VidaMax
				  && Disse(falas, "volta pro proprio corpo"), $"{Estado()} | {Ultimos(falas)}");

			ServerPlayer preso = Forjar("Preso", lugar, 1000);
			preso.Ficha.med = true;
			bool livrePode = RecusaDoMergulho(preso).Length == 0;
			preso.Zone = InteriorDoMajin.De(4242);
			Checa("DENTRO DE UM MAJIN nao se mergulha (o preso que saia do interior perdia o registro e ficava sem saida)",
				  livrePode && RecusaDoMergulho(preso).Contains("outra criatura", StringComparison.Ordinal), RecusaDoMergulho(preso));
			preso.Zone = ZonaDaBancadaDeProjetil;

			// =====================================================================
			// 8) O VIGOR DO MAJIN
			// =====================================================================
			GD.Print("[transe] -- 8) o piso de meio tanque saiu: o Majin trava no cheio e o bio, sozinho, nao tem nada (2026-10-15: `--v030teste`) --");
			static double DepoisDeUmaPassada(string raca, double vigor)
			{
				// `Statify` ANTES: e ele quem escreve a vontade e o mod de vigor que dividem o dreno -- sem eles a
				// conta divide por 0,01 e uma passada so queimaria o tanque inteiro.
				var f = new Fighter { Race = raca };
				f.Statify();
				f.maxstamina = 100;
				f.stamina = vigor;
				Nutricao.QueimarVigor(f);
				return f.stamina;
			}
			double bio = DepoisDeUmaPassada("BioAndroid", 3), humano = DepoisDeUmaPassada("Human", 3), majin = DepoisDeUmaPassada("Majin", 3);
			Checa("**O BIO-ANDROIDE NAO TEM MAIS O PISO DE MEIO TANQUE** (o dono, 2026-10-15: *\"bio androide sozinho N tem nada\"* -- o vigor dele e o das racas doadoras, medido na `--v030teste`): com 3% de vigor, a passada do estomago o deixa nos 3% (menos o dreno)",
				  bio <= 3, $"bio={bio:0.##}");
			Checa("...e o Humano e o Majin tambem: o Majin nao tem piso aqui -- o vigor dele trava NO CHEIO no tique do corpo (`Nutricao.ManterOsInfinitos`)",
				  humano <= 3 && majin <= 3, $"humano={humano:0.##} majin={majin:0.##}");

			Jandirus.Core.Races.SangueDoBio.BioComOsPassivosDeAntesDeTeste = true;
			double comPiso;
			try { comPiso = DepoisDeUmaPassada("BioAndroid", 3); }
			finally { Jandirus.Core.Races.SangueDoBio.BioComOsPassivosDeAntesDeTeste = false; }
			Checa("(defeito injetado: o bio com as passivas do DM) o piso de meio tanque volta -- os 3% viram 50% (menos o dreno): a regua de cima sabe reprovar",
				  comPiso > 49 && comPiso <= 50, $"comPiso={comPiso:0.##}");

			// =====================================================================
			// 9) O MORTO DE PE MERGULHA (v0.3.0, item 26)
			// =====================================================================
			GD.Print("[transe] -- 9) o morto de pe mergulha: a interdimensao e a excecao (dono, 2026-10-15) --");
			Checa("**A REGRA, NO CORE**: o cadaver nao mergulha e o fantasma de pe sim; e a mente e lugar de morto -- de pe la dentro, e no mundo dos vivos (sem o passe) nao",
				  DimensaoMental.PorQueNaoMergulhar(false, false, cadaver: true).Length > 0
				  && DimensaoMental.PorQueNaoMergulhar(false, false, cadaver: false).Length == 0
				  && Alem.MortoDePe(true, DimensaoMental.Zona) && !Alem.MortoDePe(false, DimensaoMental.Zona) && !Alem.MortoDePe(true, "Earth"));

			ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
			Vec2 mesa = MesaDoEnma(alem);
			Vec2 longe = MapaDaZonaOuCatalogo(alem)?.PontoLivrePerto(mesa + new Vec2(12 * ZoneCollision.TileSize, 0))
							?? mesa + new Vec2(12 * ZoneCollision.TileSize, 0);

			// O FANTASMA: morto, de pe e de aureola num lugar de morto, com a ferida de entrada -- e LONGE da mesa do Enma, pra a
			// segunda morte (que leva a mesa) nao se confundir com a volta pro corpo.
			void Fantasma(ZoneKey onde, Vec2 ponto)
			{
				if (NaMente(pl) || pl.BonecoLargado != null) SairDoTranse(pl);
				pl.Ficha.KO = false;
				pl.Combate.NocauteRestante = 0;
				foreach (BodyPart p in pl.Combate.Corpo.Partes) { p.Decepado = false; p.Vida = p.VidaMax; }
				membro.Vida = feridaDeEntrada;
				pl.Combate.SincronizarVida();
				pl.Ficha.dead = true;
				pl.MorteJaViajou = true;
				pl.RelogioDaMorte = long.MaxValue;
				pl.Ficha.passe_do_enma_ate = 0;
				pl.Ficha.hell_lockout_until = 0;
				if (!pl.Zone.Equals(onde) || !pl.Pos.Equals(ponto)) MoveToZone(pl.Id, onde, ponto);
			}

			Fantasma(alem, longe);
			bool entrouMorto = Mergulhar();
			ServerPlayer? corpoDoFantasma = pl.BonecoLargado;
			Checa("**O FANTASMA MERGULHA** (*\"permita q morto possa fazer meditacao profunda\"*): morto e de pe no Outro Mundo, a porta da mente o aceita -- ele esta numa mente, e o corpo ficou la fora",
				  entrouMorto && pl.Ficha.dead && corpoDoFantasma != null, Estado());
			if (corpoDoFantasma == null) return;
			Checa("**LA DENTRO ELE CONTINUA DE PE** (a interdimensao e a excecao): e o fantasma, e nao o cadaver -- anda, luta e treina como no Outro Mundo",
				  pl.MortoDePe && !EhCadaver(pl), $"mortoDePe={pl.MortoDePe} cadaver={EhCadaver(pl)}");
			Checa("...e o corpo que fica la fora e o dele: de pe, meditando e DE AUREOLA",
				  corpoDoFantasma.MortoDePe && corpoDoFantasma.Pose(NowMs(), false) == Protocol.Pose.Meditando
				  && Alem.TemAureola(corpoDoFantasma.Ficha.dead, corpoDoFantasma.MorteJaViajou),
				  $"pose={corpoDoFantasma.Pose(NowMs(), false)} aureola={Alem.TemAureola(corpoDoFantasma.Ficha.dead, corpoDoFantasma.MorteJaViajou)}");

			BordasDeQuemEstaFora(pl);
			TickDeQuemVolta();
			Checa("**A BORDA NAO O EXPULSA**: o `dead` dele e o da entrada, e nao uma queda -- depois da borda e da fila de volta ele continua la dentro",
				  NaMente(pl) && pl.BonecoLargado != null, Estado());
			MortoExpulsoDaMenteDeTeste = true;
			try { BordasDeQuemEstaFora(pl); TickDeQuemVolta(); }
			finally { MortoExpulsoDaMenteDeTeste = false; }
			Checa("(defeito injetado: a borda de antes) o `dead` da entrada vira queda e o fantasma e expulso no mesmo tique -- a regua de cima sabe reprovar",
				  !NaMente(pl), Estado());

			// CAIR DE VEZ LA DENTRO: o funil de toda morte (`Morrer`) num fantasma de pe -- e onde o golpe do reflexo cairia.
			Fantasma(alem, longe);
			Mergulhar();
			outro.Vida = 5;
			pl.Combate.SincronizarVida();
			bool morreuDeNovoLaDentro = pl.Combate.Morrer();
			bool desmaiouLaDentro = pl.Ficha.KO && NaMente(pl);
			BordasDeQuemEstaFora(pl);
			TickDeQuemVolta();
			Checa("**CAIR DE VEZ LA DENTRO NAO E A SEGUNDA MORTE**: o fantasma desmaia na mente, e o desmaio o devolve ao corpo -- como entrou (a ferida de entrada, o membro de dentro inteiro), de pe, no lugar do corpo, e NAO na mesa do Enma",
				  !morreuDeNovoLaDentro && desmaiouLaDentro && !NaMente(pl) && pl.Zone.Equals(alem) && pl.Pos.Equals(longe)
				  && pl.Ficha.dead && !pl.Ficha.KO && membro.Vida == feridaDeEntrada && outro.Vida == outro.VidaMax,
				  $"desmaiou={desmaiouLaDentro} {Estado()} pos={pl.Pos} (corpo {longe}, mesa {mesa})");

			// ABRIR OS OLHOS
			Fantasma(alem, longe);
			Mergulhar();
			falas = Acordar();
			Checa("**ABRIR OS OLHOS DEVOLVE O FANTASMA COMO ENTROU**: morto, de pe, sem desmaio, no lugar do corpo -- o `dead` da entrada nao vira uma 'morte de novo' na volta",
				  !NaMente(pl) && pl.Ficha.dead && pl.MortoDePe && !pl.Ficha.KO && pl.Zone.Equals(alem) && pl.Pos.Equals(longe)
				  && Disse(falas, "abre os olhos"), $"{Estado()} | {Ultimos(falas)}");

			// O GOLPE LETAL NO CORPO DE FORA
			Fantasma(alem, longe);
			Mergulhar();
			ServerPlayer corpoAbatido = pl.BonecoLargado!;
			foreach (BodyPart p in corpoAbatido.Combate.Corpo.Partes) p.Vida = 0;
			corpoAbatido.Combate.SincronizarVida();
			MarcarAgressao(corpoAbatido, agressor);
			falas = Ouvir(TickDeQuemVolta);
			Checa("**O GOLPE LETAL NO CORPO DE FORA E A SEGUNDA MORTE DO FANTASMA** (`Death.dm:113-118`): ele volta, e na volta cai de vez -- curado, na mesa do Enma, 2 s em coma",
				  !NaMente(pl) && pl.Ficha.dead && pl.Zone.Equals(alem) && Vec2.Distance(pl.Pos, mesa) < 1
				  && pl.Ficha.KO && Math.Abs(pl.Combate.NocauteRestante - Alem.SegundosDeComaDoMortoDeNovo) < 0.5
				  && membro.Vida == membro.VidaMax && Disse(falas, "mesa do Enma"),
				  $"{Estado()} pos={pl.Pos} mesa={mesa} restante={pl.Combate.NocauteRestante:0.##} | {Ultimos(falas)}");

			// REVIVIDO NO MEIO DO TRANSE: o `Reviver` de producao (o do verb de admin, que nao tira ninguem do lugar)
			Fantasma(alem, longe);
			Mergulhar();
			pl.Combate.Reviver(1, SegundosDeCarencia);
			bool vivoNaMente = NaMente(pl) && !pl.Ficha.dead;
			Acordar();
			Checa("**REVIVIDO NO MEIO DO TRANSE CONTINUA VIVO**: as esferas, um admin ou a tecnica de alguem o trazem de volta enquanto ele medita -- e abrir os olhos nao o mata de novo pela foto da entrada, nem lhe devolve as feridas do fantasma",
				  vivoNaMente && !NaMente(pl) && !pl.Ficha.dead && membro.Vida == membro.VidaMax,
				  $"vivoNaMente={vivoNaMente} {Estado()} morto={pl.Ficha.dead}");

			// A PENA DO INFERNO
			ZoneKey inferno = ZoneKey.Premade(Alem.ZonaDoInferno);
			Fantasma(inferno, PortaoDoInferno(inferno));
			pl.Ficha.hell_lockout_until = NowMs() + 600_000;
			bool entrouPreso = Mergulhar();
			CumprirPenaNoInferno(pl);
			bool ficouNaMente = NaMente(pl);
			Acordar();
			Checa("**A PENA NO INFERNO NAO O ARRANCA DA MENTE**: o corpo esta no Inferno, e e pra ele que a alma volta -- ao abrir os olhos ele esta la de novo",
				  entrouPreso && ficouNaMente && !NaMente(pl) && pl.Zone.Equals(inferno), $"entrou={entrouPreso} ficou={ficouNaMente} {Estado()}");
			pl.Ficha.hell_lockout_until = 0;

			// O CADAVER: morto no chao do mundo dos vivos, antes da viagem e sem o passe
			Fantasma(terra, lugar);
			pl.MorteJaViajou = false;
			List<string> recusa = Ouvir(() => Mergulhar());
			Checa("**O CADAVER NAO MERGULHA**: morto no chao do mundo dos vivos (antes da viagem, sem o passe), a porta recusa -- \"um corpo caído\"",
				  !NaMente(pl) && EhCadaver(pl) && Disse(recusa, "corpo caído"), Ultimos(recusa));

			// OS DEFEITOS DO JOGO DE ANTES
			Fantasma(alem, longe);
			MortoNaoMergulhaDeTeste = true;
			bool entrouComDefeito;
			try { entrouComDefeito = Mergulhar(); }
			finally { MortoNaoMergulhaDeTeste = false; }
			Checa("(defeito injetado: a porta de antes) o fantasma de pe e recusado como no DM (o `dead` de `MindMeditate.dm:355`) -- a regua de cima sabe reprovar",
				  !entrouComDefeito && !NaMente(pl), Estado());
			Mergulhar();
			Alem.MenteDeitaOMortoDeTeste = true;
			bool deitado;
			try { deitado = EhCadaver(pl) && !pl.MortoDePe; }
			finally { Alem.MenteDeitaOMortoDeTeste = false; }
			Checa("(defeito injetado: a mente nao e lugar de morto) la dentro o fantasma vira o cadaver deitado -- a regua do 'de pe' sabe reprovar",
				  NaMente(pl) && deitado, Estado());
			SairDoTranse(pl);
			Remontar();
		}
		catch (Exception e)
		{
			falhou++;
			GD.PrintErr($"[transe]   FALHA a bancada rodou inteira   {e}");
		}
		finally
		{
			BonecoDivideOCorpoDeTeste = false;
			Jandirus.Core.Races.SangueDoBio.BioComOsPassivosDeAntesDeTeste = false;
			MortoNaoMergulhaDeTeste = false;
			MortoExpulsoDaMenteDeTeste = false;
			Alem.MenteDeitaOMortoDeTeste = false;

			if (NaMente(pl) || pl.BonecoLargado != null) SairDoTranse(pl);
			DesligarBuff(pl, BuffDeAntes);
			DesligarBuff(pl, BuffDeDentro);
			_paralisadoAte.Remove(pl.Id);

			foreach (BodyPart p in pl.Combate.Corpo.Partes)
				if (corpoDeAntes.TryGetValue(p.Nome, out double[]? v) && v.Length >= 2)
				{
					p.Vida = Math.Clamp(v[0], 0, p.VidaMax);
					p.Decepado = v[1] != 0;
				}
			pl.Ficha.KO = koDeAntes;
			pl.Ficha.dead = mortoDeAntes;
			pl.MorteJaViajou = jaViajouDeAntes;
			pl.RelogioDaMorte = relogioDeAntes;
			pl.Ficha.hell_lockout_until = penaDeAntes;
			pl.Ficha.passe_do_enma_ate = passeDeAntes;
			pl.Combate.NocauteRestante = 0;
			pl.Combate.Letal = letalDeAntes;
			pl.Ficha.Ki = kiDeAntes;
			pl.Ficha.stamina = vigorDeAntes;
			pl.Ficha.med = medDeAntes;
			pl.Ficha.train = trainDeAntes;
			pl.Combate.SincronizarVida();
			LimparTudoDaBancada();
			if (!pl.Zone.Equals(zonaDeAntes) || !pl.Pos.Equals(posDeAntes)) MoveToZone(pl.Id, zonaDeAntes, posDeAntes);
			Persistir(pl);

			GD.Print($"[transe] ================ {ok} OK, {falhou} FALHA(S) ================\n");
		}
	}
}
