using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.Ranks;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ============================ OS CINCO SUPREMOS KAIOHS E AS CINCO VAGAS DO KAIOSHIN (`--kaioshinteste`, 2026-10-16) ============================
/// O dono: *"no heaven coloque npcs q sao supremos, 5 npcs q sao supremos kaios (se o majin caotic absorver eles, ele pode começar a
/// majin saga), eles sao mt fortes, em torno de 50 milhoes cada um deles (podendo ter variaçoes pequenas) , o cargo de supremo kaioh
/// pode ter ate 5 players no cargo, e cada player no cargo faz 1 npc sumir desses 5"* -- *"e esses npcs vao ser da raça kaio claro"*.
///
/// SERVIDOR PURO, NO BOOT (como a `--sagamajinteste`): os jogadores sao forjados SEM `Peer` -- nada daqui pode chegar ao
/// `Persistir` -- e tudo passa pelas portas de producao: o `ReivindicarCargo` (a mesma chamada do pedido do cliente), o
/// `Destronar`, o `TickDasMissoes`, a `Manutencao` do povoamento com a drenagem dela (`NascerNpc` + `PontoDeHabitante`), o
/// `MandarCargos` lido no fio e o gesto de absorver.
///
/// AS FAMILIAS: 1. o molde e a linha do plano · 2. os cinco no Ceu (raca, poder, nomes, cidadaos) · 3. as cinco vagas, uma por
/// reivindicacao, e o sexto recusado · 4. cada dono tira um Kaioh, na hora -- e a manutencao nao os devolve · 5. destituir UM tira
/// so a vaga dele e a manutencao devolve UM · 6. os deveres por vaga · 7. o pacote da aba · 8. o Corrupted Majin absorve um deles
/// e a saga comeca · 9. os CONTROLES: `VagasIgnoradasDeTeste` (o cargo de uma vaga de antes) e `Povoamento.DonosIgnoradosDeTeste`
/// (os donos nao tiram ninguem) -- cada um tem de reproduzir o defeito que a familia dele vigia · 10. (adendo 28) o VOO PELO PODER:
/// *"é estranho ver um kaioshin com 5 milhoes de bp n sabendo voar"* -- os cinco voam e decolam, o cidadao de 2 mil pra cima voa sem a
/// maestria do Ki, o perfil sem voo voa pelo poder, e o controle `PerfilDeCombate.PoderNaoEnsinaAVoarDeTeste`.
/// =================================================================================================================================
/// </summary>
public partial class GameServer
{
	private const int IdBaseDosKaioshins = 0x7150_0000;
	private int _kaCorpos;

	private void RodarBancadaDosKaioshins()
	{
		GD.Print("\n[kaioshin] ================ OS CINCO SUPREMOS KAIOHS E AS CINCO VAGAS DO KAIOSHIN (dono, 2026-10-16) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[kaioshin]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[kaioshin]   FALHA {nome}   [{detalhe}]"); }
		}

		ZoneKey ceu = ZoneKey.Premade(Alem.ZonaDoCeu);
		RankDef? kaioshin = Cargos.Get("kaioshin");
		var tronosAntes = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var missoesAntes = new Dictionary<string, FichaDeMissao>(_missoes, StringComparer.OrdinalIgnoreCase);
		var forjados = new List<ServerPlayer>();
		var nascidosDoVoo = new List<ServerPlayer>();

		ServerPlayer Forjar(string nome, string raca, double bp, string classe = "Normal")
		{
			int n = _kaCorpos++;
			var novo = new ServerPlayer
			{
				Id = IdBaseDosKaioshins + n,
				Peer = null,
				Name = nome,
				Race = raca,
				Class = classe,
				Genero = "Male",
				Idade = 25,
				Zone = ceu,
				Pos = PontoDeHabitante(ceu, (ulong)(900 + n)),
				Conta = $"bancada_kai_{n}",
				Slot = 0,
				Ficha = new Fighter { Race = raca, BP = bp },
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			novo.Ficha.Class = classe;
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			novo.Ficha.Tick(agoraMs: NowMs());
			forjados.Add(novo);
			return novo;
		}

		// UM CANDIDATO QUE CUMPRE O KAIOSHIN: sangue Kai com 40% de God Ki dominado e karma 80 (`Ranks.cs`, as duas regras dele).
		ServerPlayer Candidato(string nome)
		{
			ServerPlayer c = Forjar(nome, "Kai", 2_000_000);
			c.Ficha.godki = new GodKiState { awakened = true, mastery = 40 };
			c.Karma = 80;
			return c;
		}

		List<ServerPlayer> KaiohsNoCeu() =>
			[.. ZoneList(ceu.Hash).Where(p => EhNpcDoMundo(p) && HabitanteDePe(p)
				&& string.Equals(p.Papel!.Molde.Id, "supremo_kaioh", StringComparison.OrdinalIgnoreCase))];

		// A MANUTENCAO DE PRODUCAO E A DRENAGEM DELA, so pra esta linha: o resto da fila (o mundo nascendo) fica pro tique.
		void PovoarOCeu()
		{
			Manutencao();
			var meus = _filaDoPovoamento.Where(x => x.Zona.Hash == ceu.Hash && x.Molde == "supremo_kaioh").ToList();
			var resto = _filaDoPovoamento.Where(x => !(x.Zona.Hash == ceu.Hash && x.Molde == "supremo_kaioh")).ToList();
			_filaDoPovoamento.Clear();
			foreach (var x in resto) _filaDoPovoamento.Enqueue(x);
			foreach ((string m, ZoneKey z, ulong lugar) in meus) NascerNpc(m, z, PontoDeHabitante(z, lugar), lugar);
		}

		void ZerarOCargo()
		{
			if (kaioshin != null) for (int i = 0; i < 5; i++) _tronos.Remove(Vaga(kaioshin, i));   // as cinco, mesmo com o defeito de uma vaga
			foreach (ServerPlayer k in KaiohsNoCeu()) RemoverNpc(k);
		}

		string Ouvir(Action gesto)
		{
			List<string>? antes = EscutaDeAvisos;
			EscutaDeAvisos = [];
			try { gesto(); return string.Join(" | ", EscutaDeAvisos); }
			finally { EscutaDeAvisos = antes; }
		}

		try
		{
			// ------------------------------------------------------------------ 1. o molde e a linha
			MoldeDeNpc? m = _moldes?.Get("supremo_kaioh");
			Checa("1. o molde `supremo_kaioh` existe, e cidadao e e da raca Kai -- so dela", m is { Tipo: TipoDeNpc.Cidadao } && m.Racas is ["Kai"],
				$"{m?.Tipo} {string.Join(",", m?.Racas ?? [])}");
			Checa("...de 47,5 a 52,5 milhoes de BP (\"em torno de 50 milhoes\", variacao pequena)",
				m != null && m.BpMin == 47_500_000 && m.BpMax == 52_500_000, $"{m?.BpMin:N0}..{m?.BpMax:N0}");
			Checa("...com cinco nomes que nao se repetem", m is { NomesUnicos: true } && m.Nomes.Length == 5 && m.Nomes.Distinct().Count() == 5,
				string.Join(", ", m?.Nomes ?? []));
			LinhaDePovoamento? linha = _moldes?.Plano.FirstOrDefault(l => l.Molde == "supremo_kaioh");
			Checa("...e a linha do plano: CINCO no Ceu, e cada dono do Kaioshin tira um", linha is { Planeta: "Heaven", Quantos: 5, CadaDonoTiraUm: "kaioshin" },
				$"{linha?.Planeta} {linha?.Quantos} '{linha?.CadaDonoTiraUm}'");
			if (kaioshin == null || m == null || linha == null) return;

			// ------------------------------------------------------------------ 2. os cinco no Ceu
			ZerarOCargo();
			PovoarOCeu();
			List<ServerPlayer> cinco = KaiohsNoCeu();
			Checa("2. com o cargo vago, a manutencao poe CINCO Supremos Kaiohs no Ceu", cinco.Count == 5, $"{cinco.Count}");
			Checa("...todos da raca Kai, entre 47,5 e 52,5 milhoes", cinco.All(k => k.Race == "Kai" && k.Ficha.BP >= 47.5e6 && k.Ficha.BP <= 52.5e6),
				string.Join(", ", cinco.Select(k => $"{k.Race} {k.Ficha.BP / 1e6:0.0}M")));
			Checa("...com CINCO nomes diferentes", cinco.Select(k => k.Name).Distinct().Count() == 5, string.Join(", ", cinco.Select(k => k.Name)));
			Checa("...e cidadaos: nao atacam ninguem por conta propria (e revidam)", cinco.All(k => k.Papel is { Pacifico: true }));

			// ------------------------------------------------------------------ 3 e 4. as cinco vagas, e cada dono tira um
			Checa("3. o Kaioshin tem CINCO vagas -- e e o unico cargo com mais de uma", kaioshin.Vagas == 5 && Cargos.Todos.Count(r => r.Vagas > 1) == 1);
			var donos = new List<ServerPlayer>();
			var restantes = new List<int>();
			for (int i = 0; i < 5; i++)
			{
				ServerPlayer c = Candidato($"Kai {i + 1}");
				ReivindicarCargo(c, "kaioshin");
				donos.Add(c);
				restantes.Add(KaiohsNoCeu().Count);
			}
			Checa("...CINCO jogadores reivindicam o Kaioshin, e os cinco o carregam", donos.All(d => CargoDe(d.Conta) == "kaioshin"),
				string.Join(", ", donos.Select(d => $"{d.Name}={CargoDe(d.Conta)}")));
			Checa("...cada um na SUA vaga (`kaioshin`, `kaioshin#2`..`#5`), e nao sobra nenhuma", donos.Select(d => VagaDaConta(d.Conta)).Distinct().Count() == 5
				&& VagaLivre(kaioshin) == null, string.Join(", ", donos.Select(d => VagaDaConta(d.Conta))));
			ServerPlayer sexto = Candidato("Kai 6");
			string recusa = Ouvir(() => ReivindicarCargo(sexto, "kaioshin"));
			Checa("...e o SEXTO e recusado: as cinco vagas estao ocupadas", CargoDe(sexto.Conta).Length == 0 && recusa.Contains("5 vagas"), recusa);
			Checa("4. CADA DONO TIRA UM, na hora: depois de cada reivindicacao sobram 4, 3, 2, 1, 0 no Ceu",
				restantes.SequenceEqual([4, 3, 2, 1, 0]), string.Join(", ", restantes));
			PovoarOCeu();
			Checa("...e a manutencao NAO os devolve com o cargo cheio (o alvo e 5 - 5)", KaiohsNoCeu().Count == 0, $"{KaiohsNoCeu().Count}");

			// ------------------------------------------------------------------ 5. destituir um
			bool saiu = Destronar("kaioshin", "bancada", donos[2].Conta);
			Checa("5. destituir UM Kaioshin tira SO a vaga dele -- os outros quatro ficam", saiu && CargoDe(donos[2].Conta).Length == 0
				&& donos.Where((d, i) => i != 2).All(d => CargoDe(d.Conta) == "kaioshin"), string.Join(", ", donos.Select(d => CargoDe(d.Conta))));
			PovoarOCeu();
			Checa("...e a manutencao devolve UM Supremo Kaioh ao Ceu (o alvo e 5 - 4)", KaiohsNoCeu().Count == 1, $"{KaiohsNoCeu().Count}");

			// ------------------------------------------------------------------ 6. os deveres por vaga
			_proximaVoltaDasMissoes = TempoDoMundo;
			TickDasMissoes();
			var comDono = donos.Where((d, i) => i != 2).ToList();
			Checa("6. os deveres sao POR VAGA: cada Kaioshin tem a ficha dele, com o dono certo",
				comDono.All(d => _missoes.TryGetValue(VagaDaConta(d.Conta), out FichaDeMissao? f) && f.Dono == d.Conta),
				string.Join(", ", comDono.Select(d => $"{VagaDaConta(d.Conta)}:{(_missoes.TryGetValue(VagaDaConta(d.Conta), out FichaDeMissao? f) ? f.Dono : "-")}")));

			// ------------------------------------------------------------------ 7. o pacote da aba
			var ouvido = new List<(int Para, NetDataWriter Pacote)>();
			List<(int Para, NetDataWriter Pacote)>? escutaAntes = EscutaDeCargos;
			EscutaDeCargos = ouvido;
			try { MandarCargos(donos[0]); }
			finally { EscutaDeCargos = escutaAntes; }
			(string Dono, int Vagas, int Livres)? doKai = null, doMestre = null;
			if (ouvido.Count > 0)
			{
				NetDataWriter w = ouvido[^1].Pacote;
				var r = new NetDataReader(w.Data, 0, w.Length);
				if ((Protocol.S2C)r.GetByte() == Protocol.S2C.Cargos)
				{
					int n = r.GetByte();
					for (int i = 0; i < n; i++)
					{
						string ch = r.GetString(32), dono = r.GetString(200);
						r.GetString(160); r.GetString(200); r.GetString(400);
						int vagas = r.GetByte(), livres = r.GetByte();
						if (ch == "kaioshin") doKai = (dono, vagas, livres);
						if (ch == "turtle") doMestre = (dono, vagas, livres);
					}
				}
			}
			Checa("7. o pacote da aba leva os QUATRO donos do Kaioshin, 5 vagas e 1 livre",
				doKai is { } k7 && k7.Vagas == 5 && k7.Livres == 1 && comDono.All(d => k7.Dono.Split(", ").Contains(d.Name)), $"{doKai}");
			Checa("...e o cargo de uma vaga continua com UMA (o Mestre Kame)", doMestre is { Vagas: 1 }, $"{doMestre}");

			// ------------------------------------------------------------------ 8. o Majin e a saga
			ServerPlayer kaioh = KaiohsNoCeu()[0];
			ServerPlayer majin = Forjar("bancada: o Corrupted Majin", "Majin", 1e9, SagaDoMajin.Classe);
			majin.Livro.Dar(AbsorcaoMajin.Skill);
			majin.Pos = kaioh.Pos - new Vec2(ZoneCollision.TileSize, 0);
			kaioh.Combate.Nocautear(600);
			_prontoAbsorver.Remove(majin.Id);
			UsarHabilidade(majin, "absorver");
			Checa("8. o Corrupted Majin absorve um Supremo Kaioh e a SAGA COMECA (forma 1)",
				majin.Ficha.majin_saga_form == 1 && majin.Ficha.majin_kai_absorbed, $"forma {majin.Ficha.majin_saga_form}, {kaioh.Name}");
			// O DRENO DO FIM DO TIQUE, de producao: e ele que tira o absorvido do mundo (a absorcao pode chegar de dentro de um laco
			// da IA, e mexer no `_players` ali derruba o tique -- ver `GameServer.AbsorcaoMajin.cs`).
			bool marcado = kaioh.SumiuAbsorvido;
			TirarOsNpcsMortos();
			Checa("...e o Kaioh absorvido SOME do Ceu, sem cadaver (marcado `SumiuAbsorvido`, e o dreno do fim do tique o tira)",
				marcado && !_players.ContainsKey(kaioh.Id) && KaiohsNoCeu().Count == 0, $"marcado={marcado}, ainda no mundo={_players.ContainsKey(kaioh.Id)}");

			// ------------------------------------------------------------------ 9. os controles
			ZerarOCargo();
			VagasIgnoradasDeTeste = true;
			try
			{
				ServerPlayer a = Candidato("Kai A"), b = Candidato("Kai B");
				ReivindicarCargo(a, "kaioshin");
				string recusaB = Ouvir(() => ReivindicarCargo(b, "kaioshin"));
				Checa("9. CONTROLE (`VagasIgnoradasDeTeste`, o cargo de uma vaga de antes): o SEGUNDO Kaioshin fica de fora -- a familia 3 enxerga o defeito",
					CargoDe(a.Conta) == "kaioshin" && CargoDe(b.Conta).Length == 0, recusaB);
			}
			finally { VagasIgnoradasDeTeste = false; }

			ZerarOCargo();
			PovoarOCeu();
			Povoamento.DonosIgnoradosDeTeste = true;
			try
			{
				ReivindicarCargo(Candidato("Kai C"), "kaioshin");
				ReivindicarCargo(Candidato("Kai D"), "kaioshin");
				PovoarOCeu();
				Checa("...CONTROLE (`Povoamento.DonosIgnoradosDeTeste`): com dois Kaioshins os CINCO continuam no Ceu -- a familia 4 enxerga o defeito",
					KaiohsNoCeu().Count == 5 && QuantosDonos("kaioshin") == 2, $"{KaiohsNoCeu().Count} no Ceu, {QuantosDonos("kaioshin")} dono(s)");
			}
			finally { Povoamento.DonosIgnoradosDeTeste = false; }

			// ------------------------------------------------------------------ 10. o voo pelo poder (adendo 28)
			// O dono (2026-10-16): *"npcs acima de um certo nivel de poder (2k por ai) sempre vao saber voar, é estranho ver um kaioshin
			// com 5 milhoes de bp n sabendo voar"*. O molde do Kaioh sorteia `voa` 0,8: um em cinco nascia no chao.
			ZerarOCargo();
			PovoarOCeu();
			List<ServerPlayer> voam = KaiohsNoCeu();
			Checa("10. os cinco Supremos Kaiohs VOAM: o perfil diz `voa`, a porta do voo abre e o cerebro le `PodeVoar` nas capacidades",
				voam.Count == 5 && voam.All(k => k.Perfil.Voa && PodeVoar(k) && k.Perfil.Filtrar(LerCapacidades(k)).PodeVoar),
				string.Join(", ", voam.Select(k => $"{k.Name}: perfil {k.Perfil}, porta {PodeVoar(k)}")));
			if (voam.Count > 0)
			{
				ServerPlayer voador = voam[0];
				voador.Ficha.Ki = voador.Ficha.MaxKi;
				AlternarVoo(voador);
				bool subiu = voador.Voando;
				if (voador.Voando) AlternarVoo(voador);
				Checa("...e decola de verdade pelo verb de producao (`AlternarVoo`, o mesmo que o cerebro aperta)", subiu, voador.Name);
			}

			// O CIDADAO SEM A PORTA DO KI e quem mostra a linha: o Kaioh nasce com as skills no 50, e o Ki Unlocked no 50 ja abria o voo.
			ServerPlayer? cid = NascerNpc("cidadao", ceu, PontoDeHabitante(ceu, 990_001), 990_001);
			if (cid != null)
			{
				nascidosDoVoo.Add(cid);
				cid.Niveis.Por(Jandirus.Core.Skills.DegrausDoPort.SkillDoKi, 0);   // sem a maestria que abriria o voo sozinha
				cid.Ficha.BP = 1_500;
				bool fraco = PodeVoar(cid);
				cid.Ficha.BP = 3_000;
				bool forte = PodeVoar(cid);
				Checa($"...o CIDADAO sem a porta do Ki: com 1.500 de BP fica no chao, com 3.000 voa (a linha e {Jandirus.Core.Ai.PerfilDeCombate.BpQueSempreVoa:N0} de BP BASE)",
					NivelDeVoo(cid) == 0 && !DestravouPelaMaestria(cid) && !fraco && forte, $"1.500 -> {fraco}, 3.000 -> {forte}");
				Checa("...e voa com o piso de proficiencia do nivel 1 (o de quem destrava pela maestria), e nao com o do nivel 0",
					HabilidadeDeVoo(cid) >= Voo.HabilidadeNivel1, $"{HabilidadeDeVoo(cid):0.#} (piso {Voo.HabilidadeNivel1:0.#})");
			}
			else Checa("PRECONDICAO: nasce um cidadao no Ceu", false);

			var semVoo = Jandirus.Core.Ai.PerfilDeCombate.Sortear(0, 1, 4242);
			Checa("...o PERFIL: o sorteio sem voo (`voa` 0) voa de 2 mil pra cima e continua no chao abaixo -- e o ki fica como saiu",
				!semVoo.Voa && semVoo.PeloPoder(2_500).Voa && !semVoo.PeloPoder(1_500).Voa && semVoo.PeloPoder(2_500).UsaKi == semVoo.UsaKi,
				$"{semVoo} -> 2.500: {semVoo.PeloPoder(2_500)}, 1.500: {semVoo.PeloPoder(1_500)}");

			int fortes = 0, fortesVoam = 0, nascidosAqui = 0;
			for (ulong lugar = 990_100; lugar < 990_124; lugar++)
			{
				ServerPlayer? n = NascerNpc("cidadao", ceu, PontoDeHabitante(ceu, lugar), lugar);
				if (n == null) continue;
				nascidosDoVoo.Add(n);
				nascidosAqui++;
				if (n.Ficha.BP < Jandirus.Core.Ai.PerfilDeCombate.BpQueSempreVoa) continue;
				fortes++;
				if (n.Perfil.Voa && PodeVoar(n)) fortesVoam++;
			}
			Checa("...no funil de producao: todo cidadao que NASCE com 2 mil ou mais sai voando (perfil e porta)",
				fortes > 0 && fortesVoam == fortes, $"{fortesVoam} de {fortes} ({nascidosAqui} nascidos)");

			ServerPlayer semDono = Forjar("bancada: corpo que nao e do mundo", "Human", 3_000);
			Checa("...e a regra e do NPC DO MUNDO: um corpo sem molde (o do jogador, o clone), com 3.000 e sem a maestria do Ki, continua no chao",
				!EhNpcDoMundo(semDono) && !PodeVoar(semDono));

			Jandirus.Core.Ai.PerfilDeCombate.PoderNaoEnsinaAVoarDeTeste = true;
			try
			{
				bool portaComDefeito = cid != null && PodeVoar(cid);
				bool perfilComDefeito = semVoo.PeloPoder(2_500).Voa;
				Checa("...CONTROLE (`PerfilDeCombate.PoderNaoEnsinaAVoarDeTeste`, o mundo de antes): o cidadao de 3.000 fica no chao e o sorteio sem voo continua sem voo -- a familia 10 enxerga o defeito",
					cid != null && !portaComDefeito && !perfilComDefeito, $"porta {portaComDefeito}, perfil {perfilComDefeito}");
			}
			finally { Jandirus.Core.Ai.PerfilDeCombate.PoderNaoEnsinaAVoarDeTeste = false; }
		}
		finally
		{
			VagasIgnoradasDeTeste = false;
			Povoamento.DonosIgnoradosDeTeste = false;
			Jandirus.Core.Ai.PerfilDeCombate.PoderNaoEnsinaAVoarDeTeste = false;
			foreach (ServerPlayer n in nascidosDoVoo) if (_players.ContainsKey(n.Id)) RemoverNpc(n);
			foreach (ServerPlayer c in forjados)
			{
				_dentroDoMajin.Remove(c.Id);
				_prontoAbsorver.Remove(c.Id);
				_outrasMetades.Remove(c.Id);
				EsquecerTecnicas(c.Id);
				SoltarDoRaio(c.Id);
				_players.Remove(c.Id);
				ZoneList(c.Zone.Hash).Remove(c);
			}
			_tronos.Clear();
			foreach ((string k, string v) in tronosAntes) _tronos[k] = v;
			SalvarCargos();
			_missoes.Clear();
			foreach ((string k, FichaDeMissao f) in missoesAntes) _missoes[k] = f;
			SalvarMissoes();
			foreach (ServerPlayer k in KaiohsNoCeu()) RemoverNpc(k);
			PovoarOCeu();
			GD.Print($"[kaioshin] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}
}
