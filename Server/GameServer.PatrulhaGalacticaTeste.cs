using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ============================ BANCADA DA PATRULHA GALACTICA (`--patrulhateste`, 2026-10-31, o pedido 198) ============================
/// Headless e com `--host`: o host e o procurado (o crime pelo gancho de verdade pede um JOGADOR, e jogador e quem tem tela). Ela roda
/// inteira no primeiro login, sem tique do mundo no meio: o relogio anda pelo `AdiantoDoRelogioDeTeste` e o tique da Patrulha e chamado
/// a mao. Familias:
///   G1. o habitante morto pelo host (o gancho da reputacao, `MorreuUmCorpoSemDono`) vira crime: cinco = nivel 1, com o aviso;
///   G2. o planeta destruido por ele (o `ConsumarDestruicao` de verdade, num mundo sem ninguem) entra na ficha com o nome;
///   G3. a cacada: na Terra a nave pousa em 60 s (e nao antes) -- a Spacepod no chao, o esquadrao do nivel, cada patrulheiro com a armadura
///       [(2026-10-31, o pedido 202) uma Spacepod POR patrulheiro: G3c uma a uma, G3d cada uma com a cratera, G3e longe umas das outras, G3f
///       um patrulheiro de cada]
///       do exercito do Freeza, sem matar, na forca do nivel e atras do host; o planeta entra na memoria;
///   G4. a memoria e o esconderijo: num mundo sorteado que a Patrulha nunca viu, nada; de volta a Terra, a nave em 10 s;
///   G5. o nocaute pelo patrulheiro prende (no pulso seguinte): o planeta prisao, a pena do nivel, a ficha zerada, a nave e o esquadrao
///       vao embora; o campo de forca recusa a saida; fora dele com pena, o tique devolve; a pena acaba e ele sai livre;
///   G6. o patrulheiro derrubado pelo host e crime; o esquadrao inteiro derrubado: a nave decola e a proxima so em 2 min;
///   G7. a denuncia (o mundo sorteado entra na memoria; a prisao paga 25% a quem denunciou) e a entrega (100% a quem nocauteou -- e so a ele);
///   G8. o Deus da Destruicao: no nivel 5 o esquadrao derrubado chama o Bills (sem jogador no cargo), com todo o poder, atras do host; a
///       morte pelas maos dele encerra a ficha; e o Deus jogador recebe a tarefa;
///   G9. o jornal: as noticias do dia, a edicao na virada do calendario (o `S2C.Jornal` pra quem esta no jogo, por ordem de peso), o dia
///       calmo e o `jornal_ler`.
/// ===============================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	private bool _patrulhaDeTeste;

	private void RodarBancadaDaPatrulha(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DA PATRULHA GALACTICA =====");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		const int T = ZoneCollision.TileSize;
		ZoneKey terra = ZoneKey.Premade("Earth");
		ZoneKey prisao = ZoneKey.Premade(PatrulhaGalactica.ZonaDaPrisao);
		long adiantoAntes = AdiantoDoRelogioDeTeste;
		void Avancar(double segundos) => AdiantoDoRelogioDeTeste += (long)(segundos * 1000);
		bool Ouviu(string trecho) => EscutaDeAvisos?.Any(a => a.Contains(trecho, StringComparison.Ordinal)) ?? false;
		string Ultimos() => string.Join(" | ", (EscutaDeAvisos ?? []).TakeLast(3));
		// longe do ponto de chegada: la e zona segura, e ninguem luta (o pedido 113)
		Vec2 longe = PontoDeNascimento(terra) + new Vec2(30 * T, 18 * T);
		var forjados = new List<ServerPlayer>();
		ServerPlayer Forjar(string nome, Vec2 onde)
		{
			var o = new ServerPlayer
			{
				Id = _nextId++, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 25, Zone = pl.Zone, Pos = onde,
				Conta = "bancada_patrulha_" + forjados.Count, Slot = 0,   // a assinatura sai da conta e da vaga
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 1000 },
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			o.Ficha.Class = "Normal";
			PorNoMundo(o);
			forjados.Add(o);
			return o;
		}

		PatrulhaVeCorpoSemTelaDeTeste = true;
		EscutaDeAvisos = [];
		EscutaDoJornal = [];
		try
		{
			MoveToZone(pl.Id, terra, MapaDaZonaOuCatalogo(terra)?.PontoLivrePerto(longe) ?? longe);
			pl.Voando = false;
			GarantirAPatrulha();
			GarantirOJornal();

			// ============================ G1. o habitante morto vira crime
			for (int i = 0; i < 5; i++)
			{
				if (NascerNpc("cidadao", terra, pl.Pos + new Vec2((3 + i) * T, 0), 9_000UL + (ulong)i) is not { } v) continue;
				v.UltimoAgressor = pl.Id;
				v.RancorAte = NowMs() + 60_000;
				v.Ficha.dead = true;
				MorreuUmCorpoSemDono(v);
				RemoverNpc(v);
			}
			FichaDaPatrulha? f = FichaOuNulo(pl);
			string terraNome = NomeDoPlaneta("Earth");
			Checa("(o pedido 198) G1. **O HABITANTE MORTO VIRA CRIME**: cinco habitantes mortos pelo host = PROCURADO nivel 1, a ficha conta os "
				  + "cinco do planeta e ele le o aviso", NivelDeProcurado(pl) == 1 && f != null && f.MortosPorPlaneta.GetValueOrDefault(terraNome) == 5
														  && Ouviu("você agora é PROCURADO, nível 1"),
				  $"nivel {NivelDeProcurado(pl)}, {f?.Pontos} ponto(s); {Ultimos()}");

			// ============================ G0. o pulso desligado volta a andar
			// (a primeira rodada do robo: o pulso de 10 Hz se desliga sozinho sem trabalho, a bancada chamava o tique a mao, e no jogo vivo o
			// procurado nocauteado nunca ia preso.) Desligado como num servidor ocioso, o tique do ceu tem de religa-lo agora que ha procurado.
			_pulsoLigado = false;
			TickDoCalendario();
			Checa("(o pedido 198) G0. **O PULSO DA PATRULHA SE REARMA**: desligado (o servidor ocioso do boot), o tique do ceu o religa assim que "
				  + "ha um procurado -- e a cacada e a prisao andam sozinhas", _pulsoLigado);

			// ============================ G2. o planeta destruido
			double pontosAntes = f?.Pontos ?? 0;
			ZoneKey arlia = ZoneKey.Premade("Arlia");
			ConsumarDestruicao(new EstadoDaMorte
			{
				Chave = ChaveDePlaneta.Da(arlia)?.Texto ?? "", Nome = arlia.Name, Fase = FaseDaMorte.Explodindo, Estagio = 3,
				BpDoAlgoz = 1e30, IdDoAlgoz = pl.Id, Motivo = "bancada da patrulha",
			}, arlia);
			f = FichaOuNulo(pl);
			Checa("(o pedido 198) G2. **O PLANETA DESTRUIDO ENTRA NA FICHA**: o nome dele, e os 25 pontos (o host sobe de nivel)",
				  f != null && f.PlanetasDestruidos.Contains(NomeDoPlaneta(arlia.Name)) && f.Pontos >= pontosAntes + PatrulhaGalactica.PontosPorPlaneta,
				  $"{f?.Pontos} ponto(s): {(f == null ? "" : CrimesDe(f))}");

			// (2026-10-31, o pedido 202) AS NAVES DESCEM UMA A UMA: anda o relogio ate a ultima pousar -- os passos que precisam do esquadrao
			// inteiro no chao (derruba-lo todo, o Bills) passam por aqui.
			void PousarTodas()
			{
				for (int s = 0; s < 40 && _cacadas.GetValueOrDefault(pl.Id) is { NoCeu.Count: > 0 }; s++) { Avancar(0.25); PousarAsNavesDaVez(); }
			}

			// ============================ G3. a cacada na Terra
			EscutaDeAvisos.Clear();
			TickDaPatrulha();
			Cacada? c = _cacadas.GetValueOrDefault(pl.Id);
			Avancar(PatrulhaGalactica.SegundosNoPlanetaNovo - 2);
			TickDaPatrulha();
			bool cedo = c is { Pousou: true };
			int nivel = NivelDeProcurado(pl);
			double poder = Math.Max(pl.Ficha.BP, pl.Ficha.expressedBP);
			int craterasAntes = CraterasDePodDeTeste;
			Avancar(3);
			TickDaPatrulha();
			c = _cacadas.GetValueOrDefault(pl.Id);
			// (2026-10-31, o pedido 202) no instante da chegada so a PRIMEIRA nave esta no chao; as outras descem uma a uma, pelo pulso
			int noPrimeiroInstante = c?.Naves.Count ?? 0, aindaNoCeu = c?.NoCeu.Count ?? 0;
			PousarTodas();
			int quantos = PatrulhaGalactica.QuantosPatrulheiros(nivel);
			List<Obra> naves = c == null ? [] : [.. _noChao.Where(o => c.Naves.Contains(o.Id))];
			List<ServerPlayer> esquadrao = c == null ? [] : [.. c.Patrulheiros.Select(id => _players.GetValueOrDefault(id)).OfType<ServerPlayer>()];
			double raioMaximo = (PatrulhaGalactica.TilesDoPouso + (PatrulhaGalactica.AneisDoPouso - 1) * PatrulhaGalactica.TilesEntreAneis + 2) * T;
			Checa("(o pedido 198) G3. **NA TERRA A PATRULHA CHEGA EM 60 s, E NAO ANTES**: (o pedido 202) UMA SPACEPOD POR PATRULHEIRO no chao perto "
				  + "do host, e o esquadrao do nivel", !cedo && c is { Pousou: true } && naves.Count == quantos
				  && naves.TrueForAll(n => n.Tipo == "Spacepod" && n.Zona.Equals(terra) && (new Vec2(n.X, n.Y) - pl.Pos).Length <= raioMaximo)
				  && esquadrao.Count == quantos && Ouviu("naves da Patrulha descem do céu perto de você"),
				  $"cedo={cedo}, nivel {nivel}, {naves.Count} nave(s), {esquadrao.Count} patrulheiro(s); {Ultimos()}");
			Checa("(o pedido 202) G3c. **AS NAVES POUSAM UMA A UMA, DESSINCRONIZADAS**: no instante da chegada so a primeira esta no chao, e as "
				  + "outras vem depois", quantos > 1 && noPrimeiroInstante == 1 && aindaNoCeu == quantos - 1,
				  $"{noPrimeiroInstante} no chao, {aindaNoCeu} no ceu, de {quantos}");
			Checa("(o pedido 202) G3d. **CADA NAVE ABRE A SUA CRATERA** (a dos pods que chegam do espaco)", CraterasDePodDeTeste - craterasAntes == quantos,
				  $"{CraterasDePodDeTeste - craterasAntes} cratera(s) de {quantos}");
			bool espalhadas = true;
			for (int i = 0; i < naves.Count; i++)
				for (int j = i + 1; j < naves.Count; j++)
					if (Vec2.Distance(new Vec2(naves[i].X, naves[i].Y), new Vec2(naves[j].X, naves[j].Y)) < PatrulhaGalactica.TilesEntrePods * T) espalhadas = false;
			Checa("(o pedido 202) G3e. **NENHUMA NAVE POUSA PERTO DE OUTRA** (7 tiles ou mais entre elas: a cratera de uma nao encosta na da outra)",
				  naves.Count == quantos && espalhadas, $"{naves.Count} nave(s), espalhadas={espalhadas}");
			bool umPorNave = naves.Count > 0 && naves.TrueForAll(n => esquadrao.Count(o => Vec2.Distance(o.Pos, new Vec2(n.X, n.Y)) <= 2.5f * T) == 1);
			Checa("(o pedido 202) G3f. **DE CADA NAVE DESCE UM PATRULHEIRO** (ao lado dela)", umPorNave && esquadrao.Count == naves.Count,
				  $"{esquadrao.Count} patrulheiro(s) pra {naves.Count} nave(s)");
			bool moldes = esquadrao.Count > 0 && esquadrao.TrueForAll(o => o.Papel?.Molde.Id == PatrulhaGalactica.MoldeDoPatrulheiro);
			bool naoLetais = esquadrao.Count > 0 && esquadrao.TrueForAll(o => !o.Combate.Letal);
			bool naForca = esquadrao.Count > 0 && esquadrao.TrueForAll(o => Math.Abs(o.Ficha.BP - poder * PatrulhaGalactica.Forca(nivel)) <= poder * 0.02 + 1);
			bool atrasDele = esquadrao.Count > 0 && esquadrao.TrueForAll(o => GuiarOPatrulheiro(o, out ServerPlayer? presa, out _) && presa == pl);
			Checa("(o pedido 198) G3b. **OS PATRULHEIROS**: o molde da Patrulha, o golpe que NAO MATA, a forca do nivel sobre o poder do host, e a "
				  + "presa e o host", moldes && naoLetais && naForca && atrasDele,
				  $"molde={moldes} naoLetal={naoLetais} forca={naForca} ({string.Join(",", esquadrao.Select(o => o.Ficha.BP.ToString("N0")))} x {poder:N0}) presa={atrasDele}");
			bool armados = _visual != null && esquadrao.Count > 0 && esquadrao.TrueForAll(o => PatrulhaGalactica.Armaduras.Any(a =>
				RoupaDeNpc.Vestir(_visual, o.Race, o.Genero, 0, [a]).Select(p => p.Caminho).ToHashSet().SetEquals(o.Visual.Roupa.Select(p => p.Caminho))));
			Checa("(o pedido 198) G3c. **A ARMADURA DO EXERCITO DO FREEZA**: cada patrulheiro veste uma das quatro da tabela do soldado, e as racas variam",
				  armados, $"racas: {string.Join(",", esquadrao.Select(o => o.Race))}");
			Checa("(o pedido 198) G3d. **O PLANETA ENTRA NA MEMORIA DA PATRULHA**", FichaOuNulo(pl)?.Avistado.Contains(ChaveDoAvistamento(terra)) == true);

			// ============================ G4. o esconderijo e a memoria
			ZoneKey sorteado = ZoneKey.Procedural("Mundo da Patrulha", 0x6199_198UL);
			ZoneKey zonaDeVerdade = pl.Zone;
			pl.Zone = sorteado;   // so a zona: o tique da Patrulha pergunta por ela, e nada mais roda no meio da bancada
			TickDaPatrulha();
			bool encerrou = !_cacadas.ContainsKey(pl.Id) && esquadrao.TrueForAll(o => !_players.ContainsKey(o.Id))
							&& naves.Count > 0 && !_noChao.Any(o => naves.Contains(o));   // (o pedido 202) todas as naves decolaram
			Avancar(PatrulhaGalactica.SegundosNoPlanetaNovo + 5);
			TickDaPatrulha();
			bool escondido = !_cacadas.ContainsKey(pl.Id);
			Checa("(o pedido 198) G4. **NO MUNDO SORTEADO QUE A PATRULHA NUNCA VIU, ELE SE ESCONDE**: a nave da Terra decolou com o esquadrao, e "
				  + "la nada pousa nem a caminho", encerrou && escondido, $"encerrou={encerrou} escondido={escondido}");
			pl.Zone = zonaDeVerdade;
			EscutaDeAvisos.Clear();
			long agora = NowMs();
			TickDaPatrulha();
			c = _cacadas.GetValueOrDefault(pl.Id);
			Checa("(o pedido 198) G4b. **DE VOLTA A TERRA, ONDE JA FOI VISTO, A NAVE VEM EM 10 s**",
				  c != null && c.ChegaEm - agora <= (long)(PatrulhaGalactica.SegundosNoPlanetaConhecido * 1000) + 50 && Ouviu("você já foi visto neste planeta"),
				  $"chega em {(c == null ? -1 : (c.ChegaEm - agora) / 1000.0):0.0} s; {Ultimos()}");
			Avancar(PatrulhaGalactica.SegundosNoPlanetaConhecido + 1);
			TickDaPatrulha();
			c = _cacadas.GetValueOrDefault(pl.Id);
			esquadrao = c == null ? [] : [.. c.Patrulheiros.Select(id => _players.GetValueOrDefault(id)).OfType<ServerPlayer>()];
			List<int> navesDaPrisao = c?.Naves.ToList() ?? [];

			// ============================ G5. o nocaute pelo patrulheiro prende
			nivel = NivelDeProcurado(pl);
			EscutaDeAvisos.Clear();
			if (esquadrao.FirstOrDefault() is { } guarda)
			{
				pl.Ficha.KO = true;
				AoPerderALuta(pl, guarda, morreu: false);
			}
			bool pendente = pl.Zone.Equals(terra);
			CumprirAsPrisoesPendentes();
			f = FichaOuNulo(pl);
			long pena = (long)(PatrulhaGalactica.MinutosDaPena(nivel) * 60_000);
			Checa("(o pedido 198) G5. **O NOCAUTE PELO PATRULHEIRO PRENDE**: no pulso seguinte (e nao no meio do golpe), o host acorda no planeta "
				  + "prisao, com a pena do nivel e a ficha zerada", esquadrao.Count > 0 && pendente && pl.Zone.Equals(prisao) && !pl.Ficha.KO
					 && f != null && Math.Abs(f.PresoAte - NowMs() - pena) < 2000 && f.Pontos == 0 && Ouviu("você foi preso"),
				  $"zona {pl.Zone.Name}, KO {pl.Ficha.KO}, pena {(f == null ? 0 : (f.PresoAte - NowMs()) / 60000.0):0.0} min (nivel {nivel}); {Ultimos()}");
			Checa("(o pedido 198) G5b. ...e a nave e o esquadrao vao embora", !_cacadas.ContainsKey(pl.Id)
				  && esquadrao.TrueForAll(o => !_players.ContainsKey(o.Id)) && navesDaPrisao.Count > 0 && !_noChao.Any(o => navesDaPrisao.Contains(o.Id)));
			string recusa = PorQueAsEsferasPrendem(pl);
			Checa("(o pedido 198) G5c. **O CAMPO DE FORCA**: a porta de toda saida do planeta (decolar, nave, teleporte) recusa, e diz quanto falta",
				  recusa.Contains("campo de força", StringComparison.Ordinal) && recusa.Contains("faltam", StringComparison.Ordinal), recusa);
			MoveToZone(pl.Id, terra, MapaDaZonaOuCatalogo(terra)?.PontoLivrePerto(longe) ?? longe);
			TickDaPatrulha();
			Checa("(o pedido 198) G5d. **FORA DA PRISAO COM PENA, O TIQUE O DEVOLVE**", pl.Zone.Equals(prisao) && Ouviu("a sua pena ainda não acabou"),
				  $"zona {pl.Zone.Name}");
			Avancar(pena / 1000.0 + 2);
			TickDaPatrulha();
			Checa("(o pedido 198) G5e. **A PENA ACABA E ELE SAI LIVRE**: a ficha sem pena, o aviso, e o campo de forca deixa passar",
				  FichaOuNulo(pl) is { PresoAte: 0 } && Ouviu("a sua pena acabou") && PorQueAsEsferasPrendem(pl).Length == 0);

			// ============================ G6. resistir e derrubar o esquadrao
			MoveToZone(pl.Id, terra, MapaDaZonaOuCatalogo(terra)?.PontoLivrePerto(longe) ?? longe);
			for (int i = 0; i < (int)PatrulhaGalactica.Limiares[1]; i++) CrimeContraOPovo(pl, "Earth", false);
			TickDaPatrulha();
			Avancar(PatrulhaGalactica.SegundosNoPlanetaConhecido + 1);
			TickDaPatrulha();
			PousarTodas();
			c = _cacadas.GetValueOrDefault(pl.Id);
			esquadrao = c == null ? [] : [.. c.Patrulheiros.Select(id => _players.GetValueOrDefault(id)).OfType<ServerPlayer>()];
			f = FichaOuNulo(pl);
			double antesDaResistencia = f?.Pontos ?? 0;
			foreach (ServerPlayer o in esquadrao)
			{
				o.Ficha.KO = true;
				o.UltimoAgressor = pl.Id;
				AoPerderALuta(o, pl, morreu: false);
			}
			Checa("(o pedido 198) G6. **DERRUBAR UM PATRULHEIRO E CRIME** (resistir a prisao): cada um soma na ficha",
				  esquadrao.Count > 0 && f != null && f.PatrulheirosDerrotados == esquadrao.Count
				  && Math.Abs(f.Pontos - (antesDaResistencia + esquadrao.Count * PatrulhaGalactica.PontosPorPatrulheiro)) < 1e-9,
				  $"{esquadrao.Count} derrubado(s), {f?.PatrulheirosDerrotados} na ficha, {antesDaResistencia} -> {f?.Pontos}");
			EscutaDeAvisos.Clear();
			TickDaPatrulha();
			c = _cacadas.GetValueOrDefault(pl.Id);
			bool recolheu = c is { Pousou: false, Naves.Count: 0 } && esquadrao.TrueForAll(o => !_players.ContainsKey(o.Id)) && Ouviu("o esquadrão caiu");
			Avancar(PatrulhaGalactica.SegundosEntreEsquadroes - 2);
			TickDaPatrulha();
			bool antesDosDois = c is { Pousou: false };
			Avancar(3);
			TickDaPatrulha();
			Checa("(o pedido 198) G6b. **O ESQUADRAO DERRUBADO: A NAVE DECOLA, E A PROXIMA SO EM 2 MIN**",
				  recolheu && antesDosDois && c is { Pousou: true }, $"recolheu={recolheu} esperou={antesDosDois} pousou={c?.Pousou}");

			// ============================ G7. a denuncia e a entrega
			ServerPlayer delator = Forjar("bancada: o delator", pl.Pos + new Vec2(2 * T, 0));
			pl.Zone = sorteado;
			delator.Zone = sorteado;
			TickDaPatrulha();
			bool escondidoDeNovo = !_cacadas.ContainsKey(pl.Id);
			EscutaDeAvisos.Clear();
			Verbo(delator, VerboDenunciar, "");
			agora = NowMs();
			TickDaPatrulha();
			c = _cacadas.GetValueOrDefault(pl.Id);
			Checa("(o pedido 198) G7. **A DENUNCIA REVELA O MUNDO SORTEADO**: a Patrulha anota onde ele esta, e vem em 10 s",
				  escondidoDeNovo && FichaOuNulo(pl)?.Avistado.Contains(ChaveDoAvistamento(sorteado)) == true && c != null && c.Zona.Equals(sorteado)
				  && c.ChegaEm - agora <= (long)(PatrulhaGalactica.SegundosNoPlanetaConhecido * 1000) + 50 && Ouviu("você denuncia"),
				  $"escondido antes={escondidoDeNovo}, cacada {(c == null ? "nenhuma" : c.Zona.Name)}; {Ultimos()}");
			pl.Zone = zonaDeVerdade;
			delator.Zone = zonaDeVerdade;
			TickDaPatrulha();   // a cacada do mundo sorteado acaba; a da Terra comeca (conhecida)
			Avancar(PatrulhaGalactica.SegundosNoPlanetaConhecido + 1);
			TickDaPatrulha();
			c = _cacadas.GetValueOrDefault(pl.Id);
			nivel = NivelDeProcurado(pl);
			double zeniDoDelator = delator.Ficha.Zeni;
			if (c?.Patrulheiros.Select(id => _players.GetValueOrDefault(id)).OfType<ServerPlayer>().FirstOrDefault() is { } outroGuarda)
			{
				pl.Ficha.KO = true;
				AoPerderALuta(pl, outroGuarda, morreu: false);
				CumprirAsPrisoesPendentes();
			}
			double parte = PatrulhaGalactica.Recompensa(nivel) * PatrulhaGalactica.ParteDaDenuncia;
			Checa("(o pedido 198) G7b. **A PRISAO PAGA 25% A QUEM DENUNCIOU**", pl.Zone.Equals(prisao) && Math.Abs(delator.Ficha.Zeni - zeniDoDelator - parte) < 1,
				  $"nivel {nivel}: {zeniDoDelator:N0} -> {delator.Ficha.Zeni:N0} (esperado +{parte:N0})");
			Avancar(PatrulhaGalactica.MinutosDaPena(nivel) * 60 + 2);
			TickDaPatrulha();

			MoveToZone(pl.Id, terra, MapaDaZonaOuCatalogo(terra)?.PontoLivrePerto(longe) ?? longe);
			for (int i = 0; i < (int)PatrulhaGalactica.Limiares[0]; i++) CrimeContraOPovo(pl, "Earth", false);
			nivel = NivelDeProcurado(pl);
			ServerPlayer heroi = Forjar("bancada: o cacador", pl.Pos + new Vec2(2 * T, 0));
			ServerPlayer oportunista = Forjar("bancada: o oportunista", pl.Pos + new Vec2(-2 * T, 0));
			Verbo(heroi, VerboDenunciar, "");   // ele denuncia E entrega: leva a entrega so, e nao os 25% por cima
			pl.Ficha.KO = true;
			pl.UltimoAgressor = heroi.Id;
			EscutaDeAvisos.Clear();
			Verbo(oportunista, VerboChamar, "");
			bool recusouOOportunista = pl.Zone.Equals(terra) && Ouviu("só quem o nocauteou o entrega");
			double zeniDoHeroi = heroi.Ficha.Zeni;
			Verbo(heroi, VerboChamar, "");
			Checa("(o pedido 198) G7c. **A ENTREGA PAGA 100% A QUEM NOCAUTEOU** -- e so a ele: o oportunista do lado ouve que nao foi ele, e quem "
				  + "tambem o denunciou nao leva os 25% por cima",
				  recusouOOportunista && pl.Zone.Equals(prisao) && Math.Abs(heroi.Ficha.Zeni - zeniDoHeroi - PatrulhaGalactica.Recompensa(nivel)) < 1,
				  $"recusou={recusouOOportunista}, zona {pl.Zone.Name}, {zeniDoHeroi:N0} -> {heroi.Ficha.Zeni:N0}");
			Avancar(PatrulhaGalactica.MinutosDaPena(nivel) * 60 + 2);
			TickDaPatrulha();

			// ============================ G8. o Deus da Destruicao
			MoveToZone(pl.Id, terra, MapaDaZonaOuCatalogo(terra)?.PontoLivrePerto(longe) ?? longe);
			pl.Ficha.KO = false;
			for (int i = 0; i < (int)PatrulhaGalactica.Limiares[^1]; i++) CrimeContraOPovo(pl, "Earth", false);
			TickDaPatrulha();
			Avancar(PatrulhaGalactica.SegundosNoPlanetaConhecido + 1);
			TickDaPatrulha();
			PousarTodas();
			c = _cacadas.GetValueOrDefault(pl.Id);
			esquadrao = c == null ? [] : [.. c.Patrulheiros.Select(id => _players.GetValueOrDefault(id)).OfType<ServerPlayer>()];
			foreach (ServerPlayer o in esquadrao)
			{
				o.Ficha.KO = true;
				AoPerderALuta(o, pl, morreu: false);
			}
			EscutaDeAvisos.Clear();
			bool semDeus = ContaDoDeus.Length == 0;
			TickDaPatrulha();
			c = _cacadas.GetValueOrDefault(pl.Id);
			ServerPlayer? bills = c?.Patrulheiros.Select(id => _players.GetValueOrDefault(id)).OfType<ServerPlayer>()
				.FirstOrDefault(o => o.Papel?.Molde.Id == BillsEWhis.MoldeDoBills);
			Checa("(o pedido 198) G8. **NO NIVEL 5, O ESQUADRAO DERRUBADO CHAMA O DEUS DA DESTRUICAO**: sem jogador no cargo, o BILLS desce no "
				  + "planeta do host, com todo o poder, e vai atras dele", NivelDeProcurado(pl) == PatrulhaGalactica.NivelMaximo && semDeus
				  && bills != null && bills.Zone.Equals(pl.Zone) && Math.Abs(bills.Ficha.BP - BillsEWhis.PoderTotal) < 1
				  && GuiarOPatrulheiro(bills, out ServerPlayer? presaDoBills, out _) && presaDoBills == pl && Ouviu("BILLS, desce em pessoa"),
				  $"nivel {NivelDeProcurado(pl)}, sem deus={semDeus}, bills={(bills == null ? "nenhum" : bills.Ficha.BP.ToString("N0"))}; {Ultimos()}");
			if (bills != null) AoPerderALuta(pl, bills, morreu: true);
			Checa("(o pedido 198) G8b. **QUEM O DEUS ELIMINA TEM A FICHA ENCERRADA**", bills != null && NivelDeProcurado(pl) == 0,
				  $"nivel {NivelDeProcurado(pl)}");
			TickDaPatrulha();
			Checa("(o pedido 198) G8c. ...e o Bills vai embora com a cacada", bills != null && !_players.ContainsKey(bills.Id) && !_cacadas.ContainsKey(pl.Id));
			string tarefaAntes = _tarefaAlvo, nomeAntes = _tarefaNomeDoAlvo;
			long prazoAntes = _tarefaPrazo;
			DeusChamadoPelaPatrulha(pl);
			Checa("(o pedido 198) G8d. **O DEUS JOGADOR RECEBE A TAREFA**: eliminar o criminoso, com o prazo de sempre",
				  _tarefaAlvo == pl.Conta && _tarefaNomeDoAlvo == pl.Name && _tarefaPrazo > NowMs(), $"'{_tarefaAlvo}' '{_tarefaNomeDoAlvo}'");
			_tarefaAlvo = tarefaAntes;
			_tarefaNomeDoAlvo = nomeAntes;
			_tarefaPrazo = prazoAntes;
			SalvarTitulo();

			// ============================ G9. o jornal
			IReadOnlyList<Noticia> doDia = NoticiasDoDiaDeTeste;
			bool Tem(TipoDeNoticia t, string trecho) => doDia.Any(n => n.Tipo == t && n.Texto.Contains(trecho, StringComparison.Ordinal));
			Checa("(o pedido 198) G9. **AS NOTICIAS DO DIA**: o procurado e por que, o planeta destruido e por quem, a prisao, e o Deus da Destruicao",
				  Tem(TipoDeNoticia.Procurado, $"{pl.Name} é PROCURADO") && Tem(TipoDeNoticia.PlanetaDestruido, $"foi destruído por {pl.Name}")
				  && Tem(TipoDeNoticia.Preso, $"{pl.Name} foi PRESO") && Tem(TipoDeNoticia.Desequilibrio, pl.Name),
				  string.Join(" | ", doDia.Select(n => $"{n.Tipo}: {n.Texto}").Take(6)));
			int procuradosDele = doDia.Count(n => n.Tipo == TipoDeNoticia.Procurado && n.Texto.StartsWith(pl.Name + " é PROCURADO", StringComparison.Ordinal));
			Checa("(o pedido 198) G9f. **O PROCURADO QUE SOBE DE NIVEL NO DIA E UMA NOTICIA SO**: a do nivel de agora, e nao uma por degrau",
				  procuradosDele == 1, $"{procuradosDele} noticia(s) de procurado do host");
			EscutaDoJornal.Clear();
			_diaVistoNoCalendario = DiaDoCalendario - 1;
			TickDoCalendario();
			EdicaoDoJornal? edicao = UltimaEdicao;
			(bool aviso, string primeira) fio = LerOFioDoJornal(EscutaDoJornal.LastOrDefault(x => x.Para == pl.Id).Fio);
			Checa("(o pedido 198) G9b. **NA VIRADA DO DIA A EDICAO SAI**: por ordem de peso (o planeta destruido primeiro), ate o teto, e o pacote "
				  + "chega ao host com o aviso no canto", edicao != null && edicao.Dia == DiaDoCalendario && edicao.Noticias.Count is > 0 and <= NoticiasPorEdicao
				  && edicao.Noticias[0].Tipo == TipoDeNoticia.PlanetaDestruido && NoticiasDoDiaDeTeste.Count == 0
				  && fio.aviso && fio.primeira == edicao.Noticias[0].Texto,
				  $"dia {edicao?.Dia}/{DiaDoCalendario}, {edicao?.Noticias.Count} noticia(s), primeira '{edicao?.Noticias.FirstOrDefault()?.Texto}', fio aviso={fio.aviso}");
			PublicarOJornal(DiaDoCalendario + 1, "bancada");
			Checa("(o pedido 198) G9c. **O DIA SEM NADA E O DIA CALMO**", UltimaEdicao is { Noticias.Count: 1 } calmo && calmo.Noticias[0].Texto == FraseDoDiaCalmo);
			EscutaDoJornal.Clear();
			Verbo(pl, VerboJornal, "");
			fio = LerOFioDoJornal(EscutaDoJornal.LastOrDefault(x => x.Para == pl.Id).Fio);
			Checa("(o pedido 198) G9d. **O `jornal_ler` ABRE A ULTIMA EDICAO** (o pacote sem o aviso: a janela abre na hora)",
				  EscutaDoJornal.Any(x => x.Para == pl.Id) && !fio.aviso && fio.primeira == FraseDoDiaCalmo, $"aviso={fio.aviso} '{fio.primeira}'");
			Checa("(o pedido 198) G9e. **OS VERBOS CABEM NO FIO** (ate 24 letras: o servidor le o nome com `GetString(24)`)",
				  new[] { VerboDenunciar, VerboChamar, VerboFicha, VerboJornal }.All(v => v.Length <= 24));
		}
		catch (Exception e)
		{
			Checa("a bancada correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
		}
		finally
		{
			foreach (int alvo in _cacadas.Keys.ToList()) EncerrarACacada(alvo);
			foreach (ServerPlayer o in forjados) RemoverNpc(o);
			pl.Ficha.KO = false;
			if (FichaOuNulo(pl) is { } fim) { ZerarOsCrimes(fim); fim.PresoAte = 0; }
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			AdiantoDoRelogioDeTeste = adiantoAntes;
			PatrulhaVeCorpoSemTelaDeTeste = false;
			EscutaDeAvisos = null;
			EscutaDoJornal = null;
		}

		GD.Print($"===== BANCADA DA PATRULHA: {ok} OK, {falhou} FALHA =====\n");
	}

	/// <summary>Le um `S2C.Jornal` do fio: o aviso e o texto da primeira noticia.</summary>
	private static (bool Aviso, string Primeira) LerOFioDoJornal(byte[]? fio)
	{
		if (fio == null || fio.Length == 0) return (false, "");
		var r = new NetDataReader(fio);
		if (r.GetByte() != (byte)Protocol.S2C.Jornal) return (false, "");
		bool aviso = r.GetBool();
		r.GetLong();
		r.GetString();
		int n = r.GetByte();
		if (n == 0) return (aviso, "");
		r.GetByte();
		return (aviso, r.GetString());
	}
}
