using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ============================ BANCADA DO CARGO DA PATRULHA (`--cargopgteste`, 2026-10-31, o pedido 198i) ============================
/// Headless e com `--host` (o host e o candidato). Roda inteira no primeiro login: o tique da Patrulha (que carrega o do cargo) e chamado a
/// mao, e o relogio anda pelo `AdiantoDoRelogioDeTeste`. Familias:
///   K1. os requisitos: BP abaixo de 50 mil, karma zero e ficha de procurado recusam; cumpridos, ele entra e ganha o scouter (o radio);
///   K2. a primeira missao, sem procurado localizado: um foragido NPC num planeta com povo, longe dele -- e o fio do Nav leva o endereco;
///   K3. ele chega ao planeta e o foragido nasce; K4. derrotado pelo membro, a missao e cumprida (zeni e karma da Patrulha) e vem outra;
///   K5. com um procurado localizado (denunciado), a missao e ir atras dele; entregue pelo membro, cumprida;
///   K6. o membro que vira procurado e expulso; K7. sair deixa o cargo; K8. os verbos cabem no fio;
///   K9. (o pedido 200) o vilao pos-saga que invade vira a missao do membro (o aviso e dele so, e o fio do Nav leva o planeta); K9b. sem
///       missao, a primeira que vem e a do vilao; K9c. (o pedido 206) passados os dois dias sem luta ele some, e a Patrulha perde o rastro;
///       K9c2. a missao que vem nao e contra o sumido; K9c3. dias depois ele ataca outro planeta, e a missao volta; K9d. derrotado pelo
///       membro, a missao paga o zeni do vilao; K9e. e o jornal conta a derrota uma vez so, com o cargo.
/// ===============================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	private bool _cargoPgDeTeste;

	/// <summary>(bancada) o fio de cada `S2C.MissaoDaPatrulha` que saiu, por destinatario. Nulo em jogo.</summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDaMissao;

	private void RodarBancadaDoCargoDaPatrulha(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DO CARGO DA PATRULHA =====");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}
		bool Ouviu(string trecho) => EscutaDeAvisos?.Any(a => a.Contains(trecho, StringComparison.Ordinal)) ?? false;
		string Ultimos() => string.Join(" | ", (EscutaDeAvisos ?? []).TakeLast(3));
		const int T = ZoneCollision.TileSize;
		ZoneKey terra = ZoneKey.Premade("Earth");
		long adiantoAntes = AdiantoDoRelogioDeTeste;
		double bpAntes = pl.Ficha.BP, zeniAntes = pl.Ficha.Zeni;
		int karmaAntes = pl.Karma;
		var forjados = new List<ServerPlayer>();

		EscutaDeAvisos = [];
		EscutaDaMissao = [];
		PatrulhaVeCorpoSemTelaDeTeste = true;
		SorteioDaMissaoDeTeste = 1;
		try
		{
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra) + new Vec2(30 * T, 18 * T));
			GarantirAPatrulha();

			// ============================ K1. os requisitos
			pl.Ficha.BP = PatrulhaGalactica.BpParaEntrar - 1;
			pl.Karma = 10;
			Verbo(pl, VerboEntrar, "");
			bool recusouPeloBp = !MembroDaPatrulha(pl) && Ouviu("só aceita quem tem");
			pl.Ficha.BP = PatrulhaGalactica.BpParaEntrar + 1000;
			pl.Karma = 0;
			Verbo(pl, VerboEntrar, "");
			bool recusouPeloKarma = !MembroDaPatrulha(pl) && Ouviu("karma limpo");
			pl.Karma = 10;
			for (int i = 0; i < (int)PatrulhaGalactica.Limiares[0]; i++) CrimeContraOPovo(pl, "Earth", false);
			Verbo(pl, VerboEntrar, "");
			bool recusouOProcurado = !MembroDaPatrulha(pl) && Ouviu("não aceita um procurado");
			if (FichaOuNulo(pl) is { } limpa) ZerarOsCrimes(limpa);
			int scoutersAntes = pl.Mochila.Quantos(CatalogoDeItens.Scouter);
			EscutaDeAvisos.Clear();
			Verbo(pl, VerboEntrar, "");
			Checa("(o pedido 198i) K1. **OS REQUISITOS DO CARGO**: BP abaixo de 50 mil, karma zero e procurado sao recusados; cumpridos, ele entra e "
				  + "ganha o scouter (o radio da Patrulha)", recusouPeloBp && recusouPeloKarma && recusouOProcurado && MembroDaPatrulha(pl)
					 && pl.Mochila.Quantos(CatalogoDeItens.Scouter) == scoutersAntes + 1 && Ouviu("bem-vindo à Patrulha"),
				  $"bp={recusouPeloBp} karma={recusouPeloKarma} procurado={recusouOProcurado} membro={MembroDaPatrulha(pl)}; {Ultimos()}");

			// ============================ K2. a primeira missao: o foragido
			MissaoDaPatrulha? m = FichaOuNulo(pl)?.Missao;
			(bool membro, bool tem, bool naCarta, int sx, int sy, int k, ulong semente) fio = LerOFioDaMissao(EscutaDaMissao.LastOrDefault(x => x.Para == pl.Id).Fio);
			PlanetaNoEspaco? peloFio = fio.naCarta && Sistemas.Do(SeedDoUniverso, fio.sx, fio.sy) is { } sistema && fio.k < sistema.Orbitas
				? sistema.Planeta(fio.k) : null;
			Checa("(o pedido 198i) K2. **A PRIMEIRA MISSAO, SEM PROCURADO LOCALIZADO: UM FORAGIDO NPC** num planeta com povo, longe dele -- e o fio do "
				  + "botao do Nav leva o endereco desse planeta na carta", m is { Tipo: MissaoForagido } && !m.Zona.Equals(pl.Zone) && RecebeVilao(m.Zona)
					 && fio.membro && fio.tem && peloFio is { } p && p.Seed == fio.semente && Espaco.ZonaDe(p).Equals(m.Zona),
				  $"missao {m?.Tipo} em {m?.ZonaNome}; fio membro={fio.membro} tem={fio.tem} carta={fio.naCarta} -> {peloFio?.Nome}");

			// ============================ K3. o foragido nasce quando ele chega
			if (m != null) MoveToZone(pl.Id, m.Zona, PontoDeNascimento(m.Zona) + new Vec2(30 * T, 18 * T));
			EscutaDeAvisos.Clear();
			TickDaPatrulha();
			ServerPlayer? foragido = m == null ? null : _players.GetValueOrDefault(m.Npc);
			Checa("(o pedido 198i) K3. **NO PLANETA DA MISSAO, O FORAGIDO NASCE** (o vilao avulso) e o membro sabe pra que lado",
				  foragido != null && foragido.Zone.Equals(pl.Zone) && Ouviu("o foragido"), $"npc #{m?.Npc}; {Ultimos()}");

			// ============================ K4. derrotado, a missao e cumprida
			double zeni = pl.Ficha.Zeni;
			int karma = pl.Karma;
			EscutaDeAvisos.Clear();
			if (foragido != null)
			{
				foragido.Ficha.KO = true;
				AoPerderALuta(foragido, pl, morreu: false);
			}
			MissaoDaPatrulha? depois = FichaOuNulo(pl)?.Missao;
			Checa("(o pedido 198i) K4. **O FORAGIDO DERROTADO PELO MEMBRO CUMPRE A MISSAO**: o zeni e o karma da Patrulha (alem do premio do vilao), e "
				  + "vem outra missao", Ouviu("MISSÃO CUMPRIDA") && pl.Ficha.Zeni >= zeni + PatrulhaGalactica.ZeniDaMissao
					 && pl.Karma >= karma + PatrulhaGalactica.KarmaDaMissao && depois != null && depois != m,
				  $"zeni {zeni:N0} -> {pl.Ficha.Zeni:N0}, karma {karma} -> {pl.Karma}; {Ultimos()}");

			// ============================ K5. o procurado localizado
			var foragidoJogador = new ServerPlayer
			{
				Id = _nextId++, Peer = null, Name = "bancada: o procurado", Race = "Saiyan", Genero = "Male", Idade = 25, Zone = pl.Zone,
				Pos = pl.Pos + new Vec2(2 * T, 0), Conta = "bancada_cargopg_procurado", Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 5000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			foragidoJogador.Ficha.Class = "Normal";
			PorNoMundo(foragidoJogador);
			forjados.Add(foragidoJogador);
			for (int i = 0; i < (int)PatrulhaGalactica.Limiares[0]; i++) CrimeContraOPovo(foragidoJogador, pl.Zone.Name, false);
			FichaDe(foragidoJogador).Avistado.Add(ChaveDoAvistamento(foragidoJogador.Zone));   // denunciado aqui
			if (FichaOuNulo(pl) is { } fm) fm.Missao = null;
			Verbo(pl, VerboMissao, "");
			MissaoDaPatrulha? atras = FichaOuNulo(pl)?.Missao;
			bool missaoDeProcurado = atras is { Tipo: MissaoProcurado } && atras.Alvo == foragidoJogador.Assinatura && atras.Zona.Equals(foragidoJogador.Zone);
			zeni = pl.Ficha.Zeni;
			karma = pl.Karma;
			foragidoJogador.Ficha.KO = true;
			foragidoJogador.UltimoAgressor = pl.Id;
			EscutaDeAvisos.Clear();
			Verbo(pl, VerboChamar, "");
			Checa("(o pedido 198i) K5. **COM UM PROCURADO LOCALIZADO, A MISSAO E IR ATRAS DELE**: entregue pelo membro, ela e cumprida (o premio da "
				  + "Patrulha por cima da recompensa)", missaoDeProcurado && PresoNaPatrulha(foragidoJogador) && Ouviu("MISSÃO CUMPRIDA")
					 && pl.Karma >= karma + PatrulhaGalactica.KarmaDaMissao
					 && pl.Ficha.Zeni >= zeni + PatrulhaGalactica.ZeniDaMissao + PatrulhaGalactica.Recompensa(1),
				  $"missao={missaoDeProcurado} preso={PresoNaPatrulha(foragidoJogador)} zeni {zeni:N0} -> {pl.Ficha.Zeni:N0}; {Ultimos()}");

			// ============================ K6. o membro procurado e expulso
			for (int i = 0; i < (int)PatrulhaGalactica.Limiares[0]; i++) CrimeContraOPovo(pl, pl.Zone.Name, false);
			EscutaDeAvisos.Clear();
			TickDaPatrulha();
			Checa("(o pedido 198i) K6. **O MEMBRO QUE VIRA PROCURADO E EXPULSO** no segundo seguinte", !MembroDaPatrulha(pl) && Ouviu("EXPULSO"),
				  Ultimos());
			if (FichaOuNulo(pl) is { } suja) ZerarOsCrimes(suja);
			_cacadas.Remove(pl.Id);

			// ============================ K7. sair
			Verbo(pl, VerboEntrar, "");
			bool voltou = MembroDaPatrulha(pl);
			Verbo(pl, VerboSair, "");
			Checa("(o pedido 198i) K7. **SAIR DEIXA O CARGO** (e a missao)", voltou && !MembroDaPatrulha(pl) && FichaOuNulo(pl)?.Missao == null);

			Checa("(o pedido 198i) K8. **OS VERBOS DO CARGO CABEM NO FIO** (ate 24 letras)",
				  new[] { VerboEntrar, VerboSair, VerboMissao }.All(v => v.Length <= 24));

			// ============================ K9. (o pedido 200) o vilao pos-saga vira a missao da Patrulha
			// De volta ao cargo (o K7 saiu), com a missao de um foragido; o Freeza fica solto invadindo Icer -- e a missao vira a dele.
			Verbo(pl, VerboEntrar, "");
			bool atrasDoForagido = MembroDaPatrulha(pl) && FichaOuNulo(pl)?.Missao is { Tipo: MissaoForagido };
			ZoneKey icer = ZoneKey.Premade("Icer");
			EscutaDeAvisos.Clear();
			bool soltou = PalcoSV_Vencer(pl.Id, "freeza_namek", 2, icer.Name);
			VilaoSolto? solto = _soltos.Find(v => v.Elo == "freeza_namek");
			MissaoDaPatrulha? contra = FichaOuNulo(pl)?.Missao;
			var fioDoVilao = LerOFioDaMissao(EscutaDaMissao.LastOrDefault(x => x.Para == pl.Id).Fio);
			PlanetaNoEspaco? peloFioDoVilao = fioDoVilao.NaCarta && Sistemas.Do(SeedDoUniverso, fioDoVilao.Sx, fioDoVilao.Sy) is { } sv
											  && fioDoVilao.K < sv.Orbitas ? sv.Planeta(fioDoVilao.K) : null;
			Checa("(o pedido 200) K9. **O VILAO POS-SAGA QUE INVADE VIRA A MISSAO DE QUEM E DA PATRULHA**: o membro atras de um foragido passa a ir "
				  + "atras dele, no planeta que ele invade (o aviso e a missao dele), e o fio do botao do Nav leva esse planeta",
				  atrasDoForagido && soltou && solto != null && contra is { Tipo: MissaoVilao } && contra.Alvo == solto.Elo && contra.Zona.Equals(icer)
					 && Ouviu($"MISSÃO DA PATRULHA: {solto.Nome} invade") && peloFioDoVilao is { } pv && Espaco.ZonaDe(pv).Equals(icer),
				  $"foragido antes={atrasDoForagido}, solto={soltou} em {solto?.ZonaNome}; missao {contra?.Tipo} em {contra?.ZonaNome}; "
				  + $"fio -> {peloFioDoVilao?.Nome}; {Ultimos()}");

			// ============================ K9b. sem missao, a primeira que vem e a do vilao
			if (FichaOuNulo(pl) is { } semMissao) semMissao.Missao = null;
			Verbo(pl, VerboMissao, "");
			Checa("(o pedido 200) K9b. **SEM MISSAO, A QUE VEM PRIMEIRO E A DO VILAO POS-SAGA** (antes do foragido)",
				  solto != null && FichaOuNulo(pl)?.Missao is { Tipo: MissaoVilao } primeira && primeira.Alvo == solto.Elo,
				  $"veio a missao {FichaOuNulo(pl)?.Missao?.Tipo} em {FichaOuNulo(pl)?.Missao?.ZonaNome}");

			// ============================ K9c. (o pedido 206) passados os dois dias sem luta, ele some -- e a Patrulha perde o rastro
			SorteioDoPlanetaDeTeste = 0;
			EscutaDeAvisos.Clear();
			if (solto != null) AdiantoDoRelogioDeTeste += solto.SaiEm - NowMs() + 1000;
			TickDosSoltos();
			Checa("(o pedido 206) K9c. **PASSADOS OS DOIS DIAS SEM LUTA ELE SOME, E A PATRULHA PERDE O RASTRO** (o membro e avisado)",
				  solto is { Sumido: true } && Ouviu("perdeu o rastro"), $"sumido={solto?.Sumido}; {Ultimos()}");
			Checa("(o pedido 206) K9c2. **...E A MISSAO QUE VEM NAO E CONTRA O VILAO SUMIDO** (nao ha onde caca-lo)",
				  FichaOuNulo(pl)?.Missao is not { Tipo: MissaoVilao }, $"veio a missao {FichaOuNulo(pl)?.Missao?.Tipo} em {FichaOuNulo(pl)?.Missao?.ZonaNome}");

			// ============================ K9c3. dias depois ele ataca outro planeta, e a missao volta
			if (solto != null) AdiantoDoRelogioDeTeste += solto.VoltaEm - NowMs() + 1000;
			TickDosSoltos();
			Checa("(o pedido 206) K9c3. **DIAS DEPOIS ELE ATACA OUTRO PLANETA, E A MISSAO DA PATRULHA VOLTA** (no planeta novo)",
				  solto is { Sumido: false } && !solto.Zona.Equals(icer) && FichaOuNulo(pl)?.Missao is { Tipo: MissaoVilao } seguiu && seguiu.Zona.Equals(solto.Zona),
				  $"vilao em {solto?.ZonaNome}, missao {FichaOuNulo(pl)?.Missao?.Tipo} em {FichaOuNulo(pl)?.Missao?.ZonaNome}");

			// ============================ K9d. derrotado pelo membro, a missao paga o zeni do vilao
			ServerPlayer? corpoDoSolto = null;
			if (solto != null)
			{
				MoveToZone(pl.Id, solto.Zona, PontoDeNascimento(solto.Zona) + new Vec2(30 * T, 18 * T));
				TickDosSoltos();   // com gente no planeta, o corpo nasce
				corpoDoSolto = _players.GetValueOrDefault(solto.Corpo);
			}
			zeni = pl.Ficha.Zeni;
			EscutaDeAvisos.Clear();
			if (corpoDoSolto != null)
			{
				corpoDoSolto.Ficha.KO = true;
				AoPerderALuta(corpoDoSolto, pl, morreu: false);
			}
			string pago = PatrulhaGalactica.ZeniDaMissaoDeVilao.ToString("N0", Pt);
			int linhasDaDerrota = solto == null ? -1
				: NoticiasDoDiaDeTeste.Count(n => n.Tipo == TipoDeNoticia.VilaoDerrotado && n.Texto.Contains($"derrotou {solto.Nome}", StringComparison.Ordinal));
			bool comOCargo = solto != null && NoticiasDoDiaDeTeste.Any(n => n.Texto.Contains($"da Patrulha Galáctica, derrotou {solto.Nome}", StringComparison.Ordinal));
			Checa("(o pedido 200) K9d. **DERROTADO PELO MEMBRO, O VILAO POS-SAGA PAGA A MISSAO DELE** (o zeni da missao do vilao, e a missao acaba)",
				  corpoDoSolto != null && solto != null && !_soltos.Contains(solto) && Ouviu("MISSÃO CUMPRIDA") && Ouviu($"te paga {pago} zeni")
					 && pl.Ficha.Zeni >= zeni + PatrulhaGalactica.ZeniDaMissaoDeVilao && FichaOuNulo(pl)?.Missao is not { Tipo: MissaoVilao },
				  $"corpo #{corpoDoSolto?.Id}, zeni {zeni:N0} -> {pl.Ficha.Zeni:N0}; {Ultimos()}");
			Checa("(o pedido 200) K9e. **O JORNAL DO DIA CONTA A DERROTA UMA VEZ SO, COM O CARGO** (a linha da Patrulha toma o lugar da do vilao derrotado)",
				  corpoDoSolto != null && linhasDaDerrota == 1 && comOCargo,
				  $"{linhasDaDerrota} linha(s) da derrota no jornal, com o cargo={comOCargo}");
		}
		catch (Exception e)
		{
			Checa("a bancada correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}\n{e.StackTrace}");
		}
		finally
		{
			foreach (int alvo in _cacadas.Keys.ToList()) EncerrarACacada(alvo);
			foreach (ServerPlayer o in forjados) RemoverNpc(o);
			ZerarOsSoltos();   // (o K9) o Freeza solto, o corpo e a tropa
			SalvarOsSoltos();
			SorteioDoPlanetaDeTeste = null;
			if (FichaOuNulo(pl) is { } fim) { ZerarOsCrimes(fim); fim.PresoAte = 0; fim.Membro = false; fim.Missao = null; }
			pl.Ficha.BP = bpAntes;
			pl.Ficha.Zeni = zeniAntes;
			pl.Karma = karmaAntes;
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			AdiantoDoRelogioDeTeste = adiantoAntes;
			PatrulhaVeCorpoSemTelaDeTeste = false;
			SorteioDaMissaoDeTeste = null;
			EscutaDeAvisos = null;
			EscutaDaMissao = null;
		}
		GD.Print($"===== BANCADA DO CARGO DA PATRULHA: {ok} OK, {falhou} FALHA =====\n");
	}

	private static (bool Membro, bool Tem, bool NaCarta, int Sx, int Sy, int K, ulong Semente) LerOFioDaMissao(byte[]? fio)
	{
		if (fio == null || fio.Length == 0) return default;
		var r = new NetDataReader(fio);
		if (r.GetByte() != (byte)Protocol.S2C.MissaoDaPatrulha) return default;
		bool membro = r.GetBool();
		if (!membro) return (false, false, false, 0, 0, -1, 0);
		bool tem = r.GetBool();
		if (!tem) return (true, false, false, 0, 0, -1, 0);
		r.GetString();
		bool naCarta = r.GetBool();
		if (!naCarta) return (true, true, false, 0, 0, -1, 0);
		return (true, true, true, r.GetInt(), r.GetInt(), r.GetByte(), r.GetULong());
	}
}
