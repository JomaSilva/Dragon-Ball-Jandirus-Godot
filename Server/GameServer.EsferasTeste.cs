using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DAS ESFERAS DO DRAGAO (`--esferateste`).
///
/// ============================ O QUE SO DAQUI SE RESPONDE ============================
///   1. **A POSICAO E FUNCAO PURA DA SEMENTE?** As duas metades, e a segunda e a que importa: a mesma
///      entrada devolve o mesmo ponto **e** um CICLO diferente devolve um ponto DIFERENTE. So a
///      primeira ficaria verde numa funcao que devolvesse uma constante -- que e o jeito mais barato
///      de "ser deterministico" e o mais inutil.
///   2. **O ESPALHAMENTO CAI EM CHAO ANDAVEL?** Medido contra o mapa de colisao DE PRODUCAO, e nao
///      contra a intencao: a checagem pergunta ao `ZoneCollision` se a celula esta livre. Uma bancada
///      que so conferisse "o campo X foi escrito" e exatamente o cego que este projeto ja pagou.
///   3. **A ESPERA DE NASCIMENTO MORDE?** As duas metades: recem-criada a invocacao e RECUSADA, e
///      adiantado o relogio ela passa. Um prazo que nunca e atingido e indistinguivel de prazo
///      nenhum (corolario 0.7).
///   4. **A POLICIA DE PLANETA DEVOLVE?** Leva-se uma esfera pra outra zona e o TIQUE DE PRODUCAO a
///      traz de volta. Sem isto, "esfera nao sai do planeta dela" seria uma frase de comentario.
///   5. **AS TRES QUEDAS SAO UM FUNIL SO?** Nocaute derruba as sete carregadas -- e a bancada mede
///      pelo RESULTADO (`Portador == 0`), nao pela chamada.
///   6. **AS SETE SAO SETE?** Com seis, a invocacao recusa; com sete, o dragao FICA DE PE. As duas.
///   7. **O PLUGUE DA FASE 2 EXISTE DE VERDADE?** `ContarUmDesejo` consome, apaga as sete, ANDA O
///      CICLO e as espalha em posicoes NOVAS. E o unico jeito de a Fase 2 nao chegar num ponto de
///      plugue que nunca rodou.
///   8. **O CLAIM DA SUPER ESFERA FECHA -- E CAI?** Os dois sentidos: dez segundos parado conclui, e
///      afastar-se derruba. So o primeiro ficaria verde num canal que nunca cai.
///   9. **O RECADO AO DONO E O DA CONQUISTA?** A bancada le a FILA DA CONQUISTA
///      (`_recadosDeConquista`) depois de abrir uma disputa. E a prova do reuso que a tarefa pediu --
///      e o proprio DM manda reusar (`sdb_contest_channel` chama `conq_notify_owner`, :1547).
///  10. **SOBREVIVE AO REINICIO?** Ida e volta pelo `esferas.json` de verdade.
///  11. **A ESPERA DO PORUNGA CONTA DO NASCIMENTO, E NAO DO PROXIMO REINICIO?** Um mundo recem-nascido
///      sobe, tica um segundo e reinicia -- pelos carregadores e pelo tique de producao -- e o que se le
///      e o ARQUIVO: o reinicio nao tem outra coisa pra ler.
///  12. **QUEM ESTA NO PLANETA VE AS ESFERAS ACENDEREM?** O "acordado" do servidor e conta de relogio; o da
///      tela e o ultimo `S2C.Esferas` que chegou. Um set recem-nascido atravessa a espera pelas quatro portas
///      que zeram a contagem de pedidos (erguer, refazer, o Porunga de um mundo novo, o eterno refeito pelo
///      zelador) e o que se le e o FIO de quem ficou parado no planeta -- os bytes que sairam pro `Peer`
///      dele, na ordem do leitor do cliente. A quinta cena e o controle: o set que GASTOU pedidos sempre
///      avisou, e e ela que prova que a escuta enxerga um aviso quando ele sai.
/// ================================================================================
///
/// ============================ AS SETE FAMILIAS COM DEFEITO INJETADO ============================
/// As checagens acima afirmam. As familias provam que aquelas afirmacoes **sabem ficar vermelhas** --
/// e o `Mutacao` da `--provateste`, reusado.
///
///   A. **A CELULA PROIBIDA** -> reprova se alguma Super Esfera nascer a menos de
///      <see cref="SuperEsferas.CelulasNoMinimo"/> celulas de casa. Defeito injetado: o sorteio sem a
///      rejeicao -- que e literalmente o que o `rand(-8,8)` do DM faria sem o `while`.
///   B. **A ESPERA** -> reprova se a invocacao passar com o set apagado. Defeito injetado: o carimbo
///      de reativacao puxado pro passado.
///   C. **A POLICIA** -> reprova se uma esfera largada noutro mundo ficar la. Defeito injetado: a
///      zona do SET reescrita pra a zona errada -- que e o `Ballplanet` nulo do original, o furo que
///      fazia `Scatter()` rodar a cada 10 s pra sempre.
///   D. **O SAVE** -> reprova se o set nao voltar do disco com a identidade. Defeito injetado: o
///      `Ciclo` zerado no arquivo -- que e o campo que carrega ONDE as sete estao.
///   E. **O CORTE DO ZELADOR** -> reprova se o arquivo nao tiver o prazo que a memoria tem, se um
///      reinicio 1 h depois rearmar a espera, ou se um reinicio 34 h depois apagar esferas que ja
///      estavam acordadas. Defeito injetado: o zelador que corta e nao grava -- o que este port fez
///      ate 2026-10-09.
///   F. **O AVISO DE QUEM ACORDA** -> reprova se o set recem-nascido acordar e a tela de quem esta no
///      planeta continuar com a estatua e as sete apagadas. Defeito injetado: so avisa a zona o set que
///      tem pedido gasto pra zerar -- o que este port fez ate 2026-10-09.
///   G. **UMA VEZ SO** -> reprova se o aviso se repetir nos tiques seguintes. Defeito injetado: o trinco da
///      virada solto antes de cada tique -- o pacote por segundo, pra sempre, que o `Pedidos != 0` do tique
///      existia pra evitar.
/// ============================================================================================
///
///     Godot --headless --path . --host --rede 7977 --conta bancada_db --senha teste
///            --nome DbBanca --raca Namekian --esferateste
/// </summary>
public partial class GameServer
{
	private bool _esferaDeTeste;

	private delegate void ChecagemDeEsfera(string nome, bool cond, string detalhe = "");

	/// <summary>Roda uma vez, no primeiro login. MEXE no mundo e no disco -- so com a flag.</summary>
	private void RodarBancadaDasEsferas(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DAS ESFERAS DO DRAGAO =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		// Ela ergue estatuas, espalha esferas, nocauteia o proprio corpo do testador, adianta o
		// relogio do ceu e escreve nos dois arquivos. Tudo e fotografado aqui e devolvido no
		// `finally` -- senao quem rodar a bancada uma vez fica com um set fantasma pra sempre.
		// ==============================================================================
		var setsGuardados = new List<SetDeEsferas>(_sets);
		var esferasGuardadas = new List<Esfera>(_esferas);
		var supersGuardadas = _supers.Select(s => (s.Numero, s.Dono, s.DonoNome)).ToList();
		int cicloGuardado = _cicloDasSupers;
		double ceuGuardado = _adiantoDoCeu;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		string racaGuardada = pl.Race, classeGuardada = pl.Class;
		string tronoGuardado = _tronos.GetValueOrDefault("guardian", "");

		EscutaDasEsferas = [];
		EscutaDasSupers = [];

		try
		{
			// =====================================================================
			// 1. DETERMINISMO -- as duas metades
			// =====================================================================
			Vec2 a1 = SuperEsferas.PosicaoDa(SeedDoUniverso, 3, 0);
			Vec2 a2 = SuperEsferas.PosicaoDa(SeedDoUniverso, 3, 0);
			Vec2 b = SuperEsferas.PosicaoDa(SeedDoUniverso, 3, 1);
			Vec2 c = SuperEsferas.PosicaoDa(SeedDoUniverso ^ 0x1234, 3, 0);

			// IGUALDADE BIT A BIT, e nao "quase igual": determinismo que tolera epsilon nao e
			// determinismo -- e o que faz o cliente pousar "ao lado" do planeta que ele desenha.
			Checa("a posicao de uma Super Esfera e a MESMA pra (semente, numero, ciclo) iguais",
				  a1.X.Equals(a2.X) && a1.Y.Equals(a2.Y), $"{a1.X:0},{a1.Y:0} vs {a2.X:0},{a2.Y:0}");

			// ESTA E A METADE QUE PEGA A FUNCAO CONSTANTE. Sem ela, `return Vec2.Zero` passaria.
			Checa("...e MUDA quando o CICLO anda (senao 'deterministico' seria uma constante)",
				  (a1 - b).Length > 1000, $"ciclo 0 = {a1.X:0},{a1.Y:0} | ciclo 1 = {b.X:0},{b.Y:0}");

			Checa("...e MUDA com outra semente de universo",
				  (a1 - c).Length > 1000, $"{a1.X:0},{a1.Y:0} vs {c.X:0},{c.Y:0}");

			// A CELULA PROIBIDA, com o defeito injetado -- ver a familia A do cabecalho.
			bool ForaDeCasa()
			{
				for (int ciclo = 0; ciclo < 40; ciclo++)
					for (int n = 1; n <= SuperEsferas.Total; n++)
					{
						(int sx, int sy) = _sorteioSemRejeicao
							? CelulaCruaDeTeste(SeedDoUniverso, n, ciclo)
							: SuperEsferas.CelulaDa(SeedDoUniverso, n, ciclo);
						if (Math.Abs(sx) + Math.Abs(sy) < SuperEsferas.CelulasNoMinimo) return false;
					}
				return true;
			}

			MutacaoDeEsfera(Checa,
				$"nenhuma das 7 nasce a menos de {SuperEsferas.CelulasNoMinimo} celulas de casa "
				+ "(280 sorteios: 7 esferas x 40 ciclos)",
				"o sorteio SEM a rejeicao -- o `rand(-8,8)` cru do original",
				ForaDeCasa,
				() => _sorteioSemRejeicao = true,
				() => _sorteioSemRejeicao = false);

			// =====================================================================
			// 2. O SET NASCE, E CAI EM CHAO ANDAVEL
			// =====================================================================
			// O TESTADOR VIRA NAMEKUSEIJIN DO CLA DO DRAGAO E GUARDIAO DA TERRA. Nao ha atalho pro
			// `ErguerEstatua`: ela e chamada com os portoes ligados, e e por isso que os tres campos
			// precisam ser mexidos. Todos voltam no `finally`.
			// ============================ O SET ETERNO NAO SE APAGA AQUI ============================
			// A primeira versao disto era `_sets.Clear()`, e ela derrubava o Porunga de Namek junto --
			// que e o unico set que existe SEM jogador e o que a checagem 12 mede. O resultado foi uma
			// falha que nao tinha nada a ver com o set eterno: ele tinha sido apagado pela propria
			// bancada oito checagens antes.
			//
			// Vale a licao que este repo ja registrou: bancada que estraga o mundo mede o destroco que
			// ela mesma fez, e a falha aponta pro lugar errado.
			// ==================================================================================
			_sets.RemoveAll(s => !s.Eterno);
			_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
			var terra = ZoneKey.Premade("Earth");
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));

			pl.Race = "Namekian";
			pl.Class = "Dragon clan";
			_tronos["guardian"] = pl.Conta;

			ErguerEstatua(pl, "");

			SetDeEsferas? set = _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);
			Checa("o verbo de producao ergueu a Estatua do Dragao na Terra", set != null);

			// **E AQUI ESTA O FURO DO ORIGINAL FECHADO**: no DM o verb NAO chama `RecreateBalls()`.
			int nascidas = set == null ? 0 : _esferas.Count(e => e.Set == set.Id);
			Checa($"...e as {Esferas.Total} esferas nasceram JUNTO (o DM nao faz isto -- namekian.dm:95-100)",
				  nascidas == Esferas.Total, $"nasceram {nascidas}");

			ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
			int emParede = 0, foraDaZona = 0;
			if (set != null && mapa != null)
				foreach (Esfera e in _esferas.Where(x => x.Set == set.Id))
				{
					if (e.Zona.Hash != terra.Hash) foraDaZona++;
					if (mapa.BlockedAt(new Vec2(e.X, e.Y))) emParede++;
				}

			// RESULTADO E NAO INTENCAO: pergunta a COLISAO, e nao "o campo X foi escrito".
			Checa("as sete cairam em chao ANDAVEL (medido no mapa de colisao de producao)",
				  emParede == 0, $"{emParede} dentro de parede");
			Checa("...e todas as sete na zona do set", foraDaZona == 0, $"{foraDaZona} fora");

			// AS SETE EM LUGARES DIFERENTES: um espalhamento que empilhasse as sete no mesmo ponto
			// passaria em tudo acima e nao seria espalhamento nenhum.
			int distintos = set == null ? 0
				: _esferas.Where(x => x.Set == set.Id).Select(x => (x.X, x.Y)).Distinct().Count();
			Checa("...e em pontos DIFERENTES umas das outras", distintos == Esferas.Total,
				  $"{distintos} pontos distintos pra {Esferas.Total} esferas");

			// =====================================================================
			// 3. A ESPERA DE NASCIMENTO -- as duas metades, com defeito injetado
			// =====================================================================
			bool NaoInvocaApagada()
			{
				if (set == null) return false;
				JuntarAsSete(pl, set);
				int antes = _invocacoes.Count;
				InvocarODragao(pl);
				return _invocacoes.Count == antes;   // recusou: o dragao NAO subiu
			}

			Checa("recem-criadas, as sete nascem APAGADAS", set != null && !SetAtivo(set));

			MutacaoDeEsfera(Checa,
				"com o set apagado a invocacao e RECUSADA",
				"o carimbo de reativacao puxado pro passado",
				NaoInvocaApagada,
				() => { if (set != null) set.AtivoEm = TempoDoMundo - 1; },
				() => { if (set != null) set.AtivoEm = TempoDoMundo + 999999; });

			// ...E A OUTRA METADE: adiantado o relogio DE PRODUCAO, ela acorda. Sem esta, "tem espera"
			// seria indistinguivel de "nunca acorda".
			if (set != null) set.AtivoEm = TempoDoMundo + Esferas.SegundosDe(Esferas.EsperaDeNascimento);
			double falta = set == null ? 0 : set.AtivoEm - TempoDoMundo;
			_adiantoDoCeu += falta + 5;
			TickDasEsferas();
			Checa($"...e passada a espera ({falta / 3600.0:0.#} h reais) elas ACORDAM",
				  set != null && SetAtivo(set));

			// =====================================================================
			// 4. PEGAR, LARGAR, E O FUNIL DAS TRES QUEDAS
			// =====================================================================
			if (set != null)
			{
				JuntarAsSete(pl, set);
				Checa("as sete estao com o testador (o `container` do DM)",
					  QuantasCarrega(pl.Id) == Esferas.Total, $"{QuantasCarrega(pl.Id)}");

				// O NOCAUTE DERRUBA -- e a medida e o RESULTADO (`Portador == 0`), nao a chamada.
				Nocautear(pl);
				TickDasEsferas();
				Checa("NOCAUTE derruba as sete (KO.dm:75-76, e pelo funil unico deste port)",
					  QuantasCarrega(pl.Id) == 0, $"ainda com {QuantasCarrega(pl.Id)}");
				pl.Combate.Levantar();
				pl.Ficha.KO = false;
			}

			// =====================================================================
			// 5. A POLICIA DE PLANETA -- com defeito injetado
			// =====================================================================
			// ============================ O CRITERIO COMPARA COM A TERRA, E NAO COM `set.Zona` ============================
			// A primeira versao perguntava `alvo.Zona == set.Zona`, e ela era CIRCULAR: injetar o
			// defeito (a zona do set reescrita pra Namek) fazia a esfera voltar pra Namek e os dois
			// lados continuavam iguais -- a checagem passava com o sistema errado, que e exatamente o
			// que a familia existe pra impedir.
			//
			// Agora o alvo e um endereco FIXO capturado antes (a Terra, onde a estatua foi erguida), e
			// o defeito reprova: com a zona do set torta, a esfera e "devolvida" pro planeta errado.
			// =========================================================================================================
			bool VoltaProPlaneta()
			{
				if (set == null) return false;
				Esfera alvo = _esferas.First(e => e.Set == set.Id);
				alvo.Portador = 0;
				alvo.PorZona(ZoneKey.Premade("Namek"));
				alvo.X = 100; alvo.Y = 100;

				TickDasEsferas();
				return alvo.Zona.Hash == terra.Hash;
			}

			MutacaoDeEsfera(Checa,
				"esfera largada em OUTRO mundo volta sozinha pro planeta dela (`Tick`, :323-329)",
				"a zona do SET reescrita pra a zona errada -- o `Ballplanet` torto do original",
				VoltaProPlaneta,
				() => { if (set != null) set.PorZona(ZoneKey.Premade("Namek")); },
				() => { if (set != null) set.PorZona(terra); });

			// =====================================================================
			// 6. AS SETE SAO SETE -- e o dragao SOBE
			// =====================================================================
			if (set != null)
			{
				JuntarAsSete(pl, set);

				// COM SEIS: recusa. Sem esta metade, "precisa das sete" seria decoracao.
				Esfera sobrando = _esferas.First(e => e.Set == set.Id);
				sobrando.Portador = 0;
				sobrando.PorZona(ZoneKey.Premade("Namek"));   // longe de verdade, e nao "um pouco longe"

				_invocacoes.Clear();
				InvocarODragao(pl);
				Checa("com SEIS esferas a invocacao e recusada", _invocacoes.Count == 0);

				sobrando.PorZona(terra);
				sobrando.Portador = pl.Id;
				InvocarODragao(pl);
				bool subiu = _invocacoes.ContainsKey(set.Id);
				Checa("com as SETE o dragao SOBE", subiu);

				Checa("...e ele se anuncia pro mundo",
					  EscutaDasEsferas.Any(t => t.Contains("DIGA O SEU DESEJO")),
					  string.Join(" | ", EscutaDasEsferas));

				// =====================================================================
				// 7. O PLUGUE DA FASE 2 RODA DE VERDADE
				// =====================================================================
				int cicloAntes = set.Ciclo;
				var antesDoDesejo = _esferas.Where(e => e.Set == set.Id)
										   .Select(e => (e.Numero, e.X, e.Y)).ToList();

				// `ContarUmDesejo` e o funil que a Fase 2 vai chamar. Exercitar aqui e o que impede
				// aquele ponto de plugue de chegar na Fase 2 sem nunca ter rodado uma vez.
				set.Desejos = 1;
				set.Pedidos = 0;
				ContarUmDesejo(set);

				Checa("o desejo TIRA o dragao de pe", !_invocacoes.ContainsKey(set.Id));
				Checa("...poe as sete pra dormir de novo", !SetAtivo(set),
					  $"AtivoEm-agora = {set.AtivoEm - TempoDoMundo:0} s");
				Checa("...ANDA o ciclo (e o ciclo e o que faz a posicao ser pura)",
					  set.Ciclo == cicloAntes + 1, $"{cicloAntes} -> {set.Ciclo}");

				int mudaram = _esferas.Where(e => e.Set == set.Id)
					.Count(e => antesDoDesejo.First(t => t.Numero == e.Numero) is var t
								&& (Math.Abs(t.X - e.X) > 1 || Math.Abs(t.Y - e.Y) > 1));
				Checa("...e as sete estao em lugares NOVOS", mudaram >= Esferas.Total - 1,
					  $"so {mudaram} mudaram de lugar");

				// E O CAMINHO DE VOLTA: mesma seed, mesmo set, mesmo ciclo -> mesma celula. E o que
				// prova que "espalhou de novo" nao virou "sorteou em runtime".
				(int cx1, int cy1) = Esferas.CelulaDoEspalhamento(
					terra.Seed ^ Espaco.Hash64(terra.Name), set.Id, 4, set.Ciclo, 300, 300);
				(int cx2, int cy2) = Esferas.CelulaDoEspalhamento(
					terra.Seed ^ Espaco.Hash64(terra.Name), set.Id, 4, set.Ciclo, 300, 300);
				Checa("o espalhamento e reproduzivel a partir de (semente, set, numero, ciclo)",
					  cx1 == cx2 && cy1 == cy2);
			}

			// =====================================================================
			// 8. O RADAR -- as duas metades
			// =====================================================================
			if (set != null)
			{
				set.AtivoEm = TempoDoMundo - 1;
				set.Pedidos = 0;
				EscutaDasEsferas.Clear();

				// ============================ A ESCUTA DE AVISOS JA EXISTIA, E E ELA ============================
				// `Avisar` termina num `Peer.Send`, e pacote que saiu no fio nao volta pra ser
				// conferido. O `EscutaDeAvisos` (`GameServer.FormasTeste.cs:50`) e o gancho que a
				// bancada das formas ja criou pra exatamente isto -- e reusa-lo aqui e o oposto de
				// escrever um segundo `_ultimoAviso` privado desta bancada.
				// ==========================================================================================
				EscutaDeAvisos = [];

				pl.Mochila.Tirar(Jandirus.Core.Items.CatalogoDeItens.Radar, 9);
				UsarORadar(pl);
				Checa("sem o Dragon Radar na mochila o radar recusa",
					  EscutaDeAvisos.Any(t => t.Contains("não tem um Dragon Radar")),
					  string.Join(" | ", EscutaDeAvisos));

				pl.Mochila.Guardar(Jandirus.Core.Items.CatalogoDeItens.Radar);
				EscutaDeAvisos.Clear();
				UsarORadar(pl);
				Checa("com o radar ele acha as sete DESTE mundo",
					  EscutaDeAvisos.Count(t => t.Contains("estrela")) == Esferas.Total,
					  $"achou {EscutaDeAvisos.Count(t => t.Contains("estrela"))}");

				// A OUTRA METADE: esfera APAGADA nao aparece (`if(!nD.IsInactive)`, Tier 1.5.dm:241).
				// Sem ela, "o radar acha" ficaria verde num radar que acha TUDO -- e a espera entre
				// invocacoes deixaria de ser uma espera.
				set.AtivoEm = TempoDoMundo + 99999;
				EscutaDeAvisos.Clear();
				UsarORadar(pl);
				Checa("...e esfera APAGADA nao aparece no radar",
					  !EscutaDeAvisos.Any(t => t.Contains("estrela")),
					  string.Join(" | ", EscutaDeAvisos));

				set.AtivoEm = TempoDoMundo - 1;
				EscutaDeAvisos = null;
			}

			// =====================================================================
			// 9. AS SUPER ESFERAS: o claim fecha, e o claim CAI
			// =====================================================================
			foreach (SuperEsfera s in _supers) { s.Dono = ""; s.DonoNome = ""; }
			_disputasDeSuper.Clear();

			MoveToZone(pl.Id, ZonaDoEspaco, OndeEstaASuper(1));
			pl.Ficha.KO = false;
			pl.Ficha.dead = false;

			ReivindicarSuper(pl);
			Checa("encostado numa Super Esfera LIVRE, o canal de claim abre",
				  _disputasDeSuper.ContainsKey(pl.Id));

			// O CANAL CAI POR AFASTAMENTO. Esta e a metade que prova a regra de defesa inteira: quem
			// defende nao precisa vencer, so afastar. Sem ela o canal poderia nunca cair.
			pl.Pos = OndeEstaASuper(1) + new Vec2(SuperEsferas.AlcanceDaDisputa * 3, 0);
			TickDasSuperEsferas();
			Checa("...e ele CAI quando o disputante se afasta (`SDB_CONTEST_RANGE`)",
				  !_disputasDeSuper.ContainsKey(pl.Id));
			Checa("...e a esfera continua LIVRE", _supers[0].Dono.Length == 0);

			// AGORA O CLAIM COMPLETO: os dez segundos rodam no TIQUE DE PRODUCAO.
			pl.Pos = OndeEstaASuper(1);
			ReivindicarSuper(pl);
			for (int i = 0; i < (int)SuperEsferas.SegundosDeClaim + 2; i++) TickDasSuperEsferas();

			Checa($"{SuperEsferas.SegundosDeClaim:0} s parado FECHAM o claim",
				  QuantasSupersTem(pl.Assinatura) == 1,
				  $"tem {QuantasSupersTem(pl.Assinatura)}");
			Checa("...e o mundo ouve", EscutaDasSupers.Any(t => t.Contains("reivindica")),
				  string.Join(" | ", EscutaDasSupers));

			// =====================================================================
			// 10. O RECADO AO DONO E O DA CONQUISTA -- a prova do reuso
			// =====================================================================
			// A esfera passa a ser de OUTRA assinatura, e o testador tenta toma-la: o aviso tem que
			// cair na fila da CONQUISTA. Ler `_recadosDeConquista` e o que prova que nao foi criada
			// uma segunda fila -- que era o "segundo eixo pra mesma ideia" que a tarefa proibiu.
			const string outraSig = "bancada-dono-ausente";
			_supers[0].Dono = outraSig;
			_supers[0].DonoNome = "Dono Ausente";
			_recadosDeConquista.Remove(outraSig);
			_disputasDeSuper.Clear();

			pl.Pos = OndeEstaASuper(1);
			ReivindicarSuper(pl);

			Checa("tomar a esfera DE OUTRO abre o canal longo (5 min), e nao o curto",
				  _disputasDeSuper.TryGetValue(pl.Id, out DisputaDeSuper? disp)
				  && disp.Tomada && disp.Faltam > SuperEsferas.SegundosDeClaim,
				  $"faltam {_disputasDeSuper.GetValueOrDefault(pl.Id)?.Faltam:0}");

			Checa("...e o dono ausente recebe o recado NA FILA DA CONQUISTA (o `conq_notify_owner` "
				+ "que o proprio DM reusa, :1547)",
				  _recadosDeConquista.TryGetValue(outraSig, out List<string>? fila)
				  && fila.Any(t => t.Contains("Super Esfera")),
				  "a fila da conquista nao recebeu nada -- o reuso nao aconteceu");

			// E O DEFENSOR VENCE SEM LUTAR: nocautear o ladrao derruba o canal.
			Nocautear(pl);
			TickDasSuperEsferas();
			Checa("nocautear o ladrao derruba a disputa (defender e CONTROLE DE AREA, nao dano)",
				  !_disputasDeSuper.ContainsKey(pl.Id));
			Checa("...e a esfera continua com o dono", _supers[0].Dono == outraSig);
			pl.Combate.Levantar();
			pl.Ficha.KO = false;

			// O CONSUMO DAS SETE anda o ciclo e devolve todas -- o funil que a Fase 2 vai chamar.
			foreach (SuperEsfera s in _supers) { s.Dono = pl.Assinatura; s.DonoNome = pl.Name; }
			int cicloSuperAntes = _cicloDasSupers;
			Vec2 antesDoConsumo = OndeEstaASuper(1);
			ConsumirAsSupers();

			Checa("consumir as sete Super ANDA o ciclo", _cicloDasSupers == cicloSuperAntes + 1);
			Checa("...solta todos os claims", QuantasSupersTem(pl.Assinatura) == 0);
			Checa("...e as move de lugar",
				  (OndeEstaASuper(1) - antesDoConsumo).Length > 1000);

			// =====================================================================
			// 11. SOBREVIVE AO REINICIO -- com defeito injetado
			// =====================================================================
			int cicloReal = set?.Ciclo ?? 0;
			int idReal = set?.Id ?? 0;

			// ============================ O CRITERIO GUARDA O NUMERO ANTES, E NAO O RELE ============================
			// A primeira versao comparava `volta.Ciclo == set.Ciclo` -- e ela tambem era CIRCULAR:
			// zerar o ciclo no objeto vivo fazia o arquivo sair com zero, voltar com zero, e os dois
			// lados continuavam iguais. O criterio media a IDA E VOLTA e nao o VALOR.
			//
			// `cicloReal` e capturado antes de qualquer defeito, entao a afirmacao passa a ser a certa:
			// *"o set volta com O ciclo que ele tinha"*, e nao *"volta com o que estiver la"*.
			// ====================================================================================================
			bool VoltaDoDisco()
			{
				SalvarEsferas();
				var setsVivos = new List<SetDeEsferas>(_sets);
				var esferasVivas = new List<Esfera>(_esferas);

				_sets.Clear();
				_esferas.Clear();
				CarregarEsferas();

				SetDeEsferas? volta = _sets.Find(s => !s.Eterno);
				bool bom = volta != null
						&& volta.Id == idReal && volta.Ciclo == cicloReal
						&& volta.Zona.Hash == terra.Hash
						&& _esferas.Count(e => e.Set == volta.Id) == Esferas.Total;

				_sets.Clear(); _sets.AddRange(setsVivos);
				_esferas.Clear(); _esferas.AddRange(esferasVivas);
				return bom;
			}

			MutacaoDeEsfera(Checa,
				"o set volta do `esferas.json` com id, ciclo, zona e as sete",
				"o ciclo zerado no arquivo -- o campo que carrega ONDE as sete estao",
				VoltaDoDisco,
				() => { if (set != null) set.Ciclo = 0; },
				() => { if (set != null) set.Ciclo = cicloReal; });

			// =====================================================================
			// 12. O SET ETERNO DE NAMEK
			// =====================================================================
			SetDeEsferas? eterno = _sets.Find(s => s.Eterno);
			Checa("o set ETERNO existe sem jogador nenhum", eterno != null);
			if (eterno != null)
			{
				Checa($"...em {Esferas.PlanetaEterno}, e so la",
					  string.Equals(eterno.ZonaNome, Esferas.PlanetaEterno, StringComparison.OrdinalIgnoreCase),
					  eterno.ZonaNome);
				Checa($"...com {Esferas.DesejosDoEterno} pedidos (e a UNICA diferenca mecanica "
					+ "Porunga x Shenron)", eterno.Desejos == Esferas.DesejosDoEterno);
				Checa("...e o zelador o levanta se alguem o inertar",
					  InertarELevantar(eterno));
			}

			// =====================================================================
			// 13. A ESPERA DO PORUNGA CONTA DO NASCIMENTO -- com defeito injetado
			// =====================================================================
			SecaoDoEternoNoReinicio(Checa);

			// =====================================================================
			// 14. QUEM ESTA NO PLANETA VE AS ESFERAS ACENDEREM -- com defeito injetado
			// =====================================================================
			SecaoDeQuemAcorda(Checa, pl);
		}
		finally
		{
			AcordarCaladoDeTeste = false;
			_trincoSolto = false;
			EscutaDoFioDasEsferas = null;

			_sets.Clear(); _sets.AddRange(setsGuardados);
			_esferas.Clear(); _esferas.AddRange(esferasGuardadas);
			foreach ((int n, string dono, string nome) in supersGuardadas)
				if (_supers.Find(s => s.Numero == n) is { } s) { s.Dono = dono; s.DonoNome = nome; }

			_cicloDasSupers = cicloGuardado;
			_disputasDeSuper.Clear();
			_invocacoes.Clear();
			_adiantoDoCeu = ceuGuardado;
			_sorteioSemRejeicao = false;

			pl.Race = racaGuardada;
			pl.Class = classeGuardada;
			if (tronoGuardado.Length > 0) _tronos["guardian"] = tronoGuardado;
			else _tronos.Remove("guardian");

			pl.Ficha.KO = false;
			pl.Combate?.Levantar();
			MoveToZone(pl.Id, zonaGuardada, posGuardada);

			SalvarEsferas();
			SalvarSupers();
			EscutaDasEsferas = null;
			EscutaDasSupers = null;
			EscutaDeAvisos = null;   // ela e de OUTRA bancada: deixa-la ligada custaria uma lista crescendo
		}

		GD.Print($"===== BANCADA DAS ESFERAS: {ok} OK, {falhou} FALHA =====\n");
	}

	// =====================================================================
	// AS FERRAMENTAS DA BANCADA
	// =====================================================================
	/// <summary>
	/// O INTERRUPTOR DO DEFEITO DA FAMILIA A: quando ligado, a bancada usa o sorteio SEM a rejeicao.
	///
	/// Ele mora aqui e nao no Core de proposito -- o Core nao pode ter um botao de defeito. O que a
	/// familia A mede e que a REJEICAO (o `while` do `pspace_sdb_scatter`) faz diferenca; pra isso ela
	/// precisa de um "antes", e o antes e o sorteio cru.
	/// </summary>
	private bool _sorteioSemRejeicao;

	/// <summary>
	/// O SORTEIO CRU -- a primeira tentativa, sem a rejeicao. **So a bancada chama.**
	///
	/// Ele repete as tres linhas do `SuperEsferas.CelulaDa` de proposito: a familia A tem que comparar
	/// COM e SEM a guarda, e chamar o de producao com um parametro "sem guarda" poria um caminho de
	/// teste dentro do codigo de jogo -- que e pior que a repeticao de tres linhas.
	/// </summary>
	private static (int Sx, int Sy) CelulaCruaDeTeste(ulong seed, int numero, int ciclo)
	{
		const int lado = 2 * SuperEsferas.CelulasNoMaximo + 1;
		ulong h = Espaco.Misturar(seed ^ 0x8FB1A1D0C9E37B4DUL,
								  ((ulong)(uint)numero << 32) | 0u, (ulong)(uint)ciclo);
		return ((int)(h % lado) - SuperEsferas.CelulasNoMaximo,
				(int)((h >> 32) % lado) - SuperEsferas.CelulasNoMaximo);
	}

	/// <summary>Poe as sete de um set na mao do testador, sem passar pelo chao.</summary>
	private void JuntarAsSete(ServerPlayer pl, SetDeEsferas s)
	{
		foreach (Esfera e in _esferas.Where(x => x.Set == s.Id))
		{
			e.PorZona(pl.Zone);
			e.Portador = pl.Id;
			e.X = pl.Pos.X;
			e.Y = pl.Pos.Y;
		}
	}

	/// <summary>
	/// O ZELADOR DO ETERNO DESFAZ UMA INERCIA? Mede o resultado do <see cref="ManterOSetEterno"/> de
	/// producao, e nao a intencao: inerta, tiquea, e pergunta se o set voltou.
	/// </summary>
	private bool InertarELevantar(SetDeEsferas eterno)
	{
		eterno.Inerte = true;
		eterno.Desejos = 1;
		ManterOSetEterno();
		return !eterno.Inerte && eterno.Desejos == Esferas.DesejosDoEterno;
	}

	// =====================================================================
	// O REINICIO DE UM MUNDO CUJO PORUNGA ACABOU DE NASCER
	// =====================================================================
	/// <summary>
	/// **13. A ESPERA DO PORUNGA CONTA DO NASCIMENTO, E NAO DO PROXIMO REINICIO** -- a familia E do cabecalho.
	///
	/// O eterno nasce com a espera de nascimento de todo set (0,4 ano, `Dragonballs.dm:108`) e o zelador a
	/// corta pro teto dele (0,1, `:414`) na primeira manutencao. No DM esse corte mora na estatua e vai pro
	/// disco com ela (`SaveItems`, `MapSave.dm:108-114`). Aqui ele ficava so na memoria: o `esferas.json`
	/// guardava os 0,4, e o primeiro reinicio cortava DE NOVO, contando dele.
	///
	/// As duas afirmacoes da frente nao sao o conserto, e estao ali por isso: se o eterno ja nascesse dentro
	/// do teto nao haveria corte nenhum pra gravar, e as tres familias de baixo ficariam verdes de graca.
	/// </summary>
	private void SecaoDoEternoNoReinicio(ChecagemDeEsfera Checa)
	{
		const double hora = 3600;
		double nascer = Esferas.SegundosDe(Esferas.EsperaDeNascimento);
		double teto = Esferas.SegundosDe(Esferas.TetoDeEsperaEterna);

		ReinicioDoEterno m = MedirOReinicioDoEterno(1);
		ReinicioDoEterno n = MedirOReinicioDoEterno(34);
		GD.Print($"  [medido] nasce faltando {m.Nascido / hora:0.00} h | depois do 1o tique: {m.Cortado / hora:0.00} h "
			   + $"na memoria, {m.FaltaNoArquivo / hora:0.00} h no arquivo | reinicio 1 h depois: faltavam "
			   + $"{m.FaltavaAntes / hora:0.00} h, faltam {m.FaltaDepois / hora:0.00} h | reinicio 34 h depois: "
			   + $"{(n.AcordadasAntes ? "acordadas" : "apagadas")} antes, "
			   + (n.AcordadasDepois ? "acordadas depois" : $"apagadas por mais {n.FaltaDepois / hora:0.00} h depois"));

		Checa($"mundo recem-nascido: o eterno nasce apagado por {nascer / hora:0.#} h (0,4 ano, `Dragonballs.dm:108`)",
			  m.ArquivoAntesDoTique && Math.Abs(m.Nascido - nascer) <= 5, $"nasceu faltando {m.Nascido:0} s");
		Checa($"...e o primeiro tique o corta pra {teto / hora:0.#} h (o teto do zelador, `:414`)",
			  Math.Abs(m.Cortado - teto) <= 5, $"depois do tique faltam {m.Cortado:0} s");

		const string defeito = "o zelador corta e NAO grava: o corte fica so na memoria";
		Action injetar = () => ZeladorSoNaMemoriaDeTeste = true;
		Action desfazer = () => ZeladorSoNaMemoriaDeTeste = false;

		MutacaoDeEsfera(Checa,
			"o corte ESTA NO ARQUIVO: o `esferas.json` guarda o mesmo prazo que a memoria tem depois do tique",
			defeito,
			() => MedirOReinicioDoEterno(1) is var x && x.NoArquivo > 0 && x.NoArquivo == x.NaMemoria,
			injetar, desfazer);

		MutacaoDeEsfera(Checa,
			"um reinicio 1 h depois NAO rearma a espera: faltam as horas que faltavam com o servidor de pe",
			defeito,
			() => MedirOReinicioDoEterno(1) is var x && Math.Abs(x.FaltaDepois - x.FaltavaAntes) <= 5,
			injetar, desfazer);

		MutacaoDeEsfera(Checa,
			"um reinicio 34 h depois NAO apaga esferas que ja estavam ACORDADAS",
			defeito,
			() => MedirOReinicioDoEterno(34) is var x && x.AcordadasAntes && x.AcordadasDepois,
			injetar, desfazer);
	}

	/// <summary>
	/// O que um mundo cujo Porunga acabou de nascer viu em cada instante: nascer, o primeiro tique, e o
	/// reinicio. Em SEGUNDOS QUE FALTAM pro Porunga acordar, menos os dois carimbos crus do meio (memoria e
	/// arquivo, negativo = nao ha), que se comparam um com o outro.
	/// </summary>
	private readonly record struct ReinicioDoEterno(
		bool ArquivoAntesDoTique, double Nascido, double Cortado, double NaMemoria, double NoArquivo,
		double FaltavaAntes, double FaltaDepois, bool AcordadasAntes, bool AcordadasDepois)
	{
		/// <summary>Quanto o ARQUIVO dizia que faltava no instante do primeiro tique. Negativo = nao ha arquivo.</summary>
		public double FaltaNoArquivo => NoArquivo < 0 ? -1 : Cortado + (NoArquivo - NaMemoria);
	}

	/// <summary>
	/// O `AtivoEm` DO SET ETERNO COMO O `esferas.json` O GUARDA AGORA -- lido do disco, e nao da memoria.
	/// Negativo se nao ha arquivo, ou se nao ha eterno nele.
	///
	/// E a medida que serve aqui: o reinicio so tem o arquivo pra ler, e a memoria de quem cortou e nao gravou
	/// esta certa ate o processo cair.
	/// </summary>
	private double AtivoEmDoEternoNoDisco()
	{
		if (!System.IO.File.Exists(CaminhoDasEsferas)) return -1;
		LivroDasEsferas? l = System.Text.Json.JsonSerializer.Deserialize<LivroDasEsferas>(
			System.IO.File.ReadAllText(CaminhoDasEsferas),
			new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
		return l?.Sets.Find(s => s.Eterno)?.AtivoEm ?? -1;
	}

	/// <summary>
	/// O PRIMEIRO SEGUNDO E O REINICIO de um mundo cujo Porunga acabou de nascer: o tique de producao (o
	/// zelador corta a espera de nascimento), `horasDepois` horas de relogio, e o carregador de producao --
	/// que e o reinicio no que toca as esferas, e so tem o ARQUIVO pra ler.
	///
	/// Quem chama poe o mundo no ponto de partida (o primeiro boot aqui, a limpeza na `--wipeteste`) e devolve
	/// o relogio e as listas depois: o adianto fica, e a memoria sai com o que o carregador leu.
	/// </summary>
	private ReinicioDoEterno TicarEReiniciarOEterno(double horasDepois)
	{
		bool haviaArquivo = System.IO.File.Exists(CaminhoDasEsferas);
		double nascido = (_sets.Find(s => s.Eterno)?.AtivoEm ?? -1) - TempoDoMundo;

		TickDasEsferas();
		SetDeEsferas? vivo = _sets.Find(s => s.Eterno);
		double naMemoria = vivo?.AtivoEm ?? -1;
		double cortado = naMemoria - TempoDoMundo;
		double noArquivo = AtivoEmDoEternoNoDisco();

		_adiantoDoCeu += horasDepois * 3600;
		double faltava = naMemoria - TempoDoMundo;
		bool acordadasAntes = vivo != null && SetAtivo(vivo);

		CarregarEsferas();
		SetDeEsferas? relido = _sets.Find(s => s.Eterno);
		return new ReinicioDoEterno(haviaArquivo, nascido, cortado, naMemoria, noArquivo, faltava,
			(relido?.AtivoEm ?? -1) - TempoDoMundo, acordadasAntes, relido != null && SetAtivo(relido));
	}

	/// <summary>
	/// UM MUNDO RECEM-NASCIDO QUE REINICIA `horasDepois` HORAS DEPOIS. O primeiro boot e o carregador de
	/// producao numa pasta de saves vazia; o resto e o <see cref="TicarEReiniciarOEterno"/>. As listas e o
	/// relogio voltam como estavam, entao a medida pode ser repetida.
	///
	/// A PASTA VAZIA E UMA TEMPORARIA (o palco da `--porungateste`), e nao a do servidor com o arquivo
	/// apagado: apagar o `esferas.json` de quem esta de pe deixaria a pasta sem as estatuas dela ate o
	/// `finally` -- e esta bancada tambem roda em cima de pasta de verdade.
	/// </summary>
	private ReinicioDoEterno MedirOReinicioDoEterno(double horasDepois)
	{
		var setsVivos = new List<SetDeEsferas>(_sets);
		var esferasVivas = new List<Esfera>(_esferas);
		double ceuVivo = _adiantoDoCeu;

		using PalcoDeApagamentos caixa = PalcoDeApagamentosDeBancada();
		try
		{
			CarregarEsferas();
			return TicarEReiniciarOEterno(horasDepois);
		}
		finally
		{
			_adiantoDoCeu = ceuVivo;
			_sets.Clear(); _sets.AddRange(setsVivos);
			_esferas.Clear(); _esferas.AddRange(esferasVivas);
		}
	}

	// =====================================================================
	// QUEM ESTA NO PLANETA VE AS ESFERAS ACENDEREM
	// =====================================================================
	/// <summary>
	/// O INTERRUPTOR DO DEFEITO DA FAMILIA G: a bancada solta o trinco da virada
	/// (<see cref="SetDeEsferas.VistoAcordado"/>) antes de cada tique que vem depois de o set acordar.
	///
	/// Mora aqui pelo motivo do <see cref="_sorteioSemRejeicao"/>: o que a familia mede e que o TRINCO faz
	/// diferenca, e o "sem trinco" e um tique que ve a virada de novo a cada segundo. Um segundo botao de defeito
	/// no codigo de jogo so pra isso custaria mais que a linha daqui.
	/// </summary>
	private bool _trincoSolto;

	/// <summary>
	/// **14. O SET QUE ACORDA AVISA A ZONA, UMA VEZ** -- as familias F e G do cabecalho.
	///
	/// No DM cada esfera troca o proprio `icon_state` quando a espera vence (`Tick()`, `Dragonballs.dm:282-286`,
	/// rearmado de 10 em 10 s em `:330`): quem esta olhando ve acender. Aqui o desenho so muda quando chega um
	/// `S2C.Esferas` novo, e o pacote leva o bit de apagada calculado na hora do envio -- o servidor pode
	/// responder ACORDADAS no `db_ver`, o radar pode achar as sete, e a tela continuar com o retrato de nascimento.
	///
	/// AS PORTAS SAO AS QUE ZERAM A CONTAGEM DE PEDIDOS, e sao quatro: `ErguerEstatua`, `RefazerOSet`, o
	/// `ErguerOSetEterno` de um mundo novo e o zelador refazendo as sete do eterno. Todas passam pelo
	/// `RefazerAsEsferas`. O set que GASTOU os pedidos e a quinta cena e e o controle: esse sempre avisou.
	///
	/// O TESTADOR FICA PARADO NO PLANETA do nascimento ao fim de cada cena: reentrar na zona reenvia o pacote, e
	/// esconderia justamente o que ela veio medir.
	/// </summary>
	private void SecaoDeQuemAcorda(ChecagemDeEsfera Checa, ServerPlayer pl)
	{
		var terra = ZoneKey.Premade("Earth");
		var namek = ZoneKey.Premade(Esferas.PlanetaEterno);

		// O PALCO: nenhum dragao de pe, nenhuma esfera na mao de ninguem, e so o eterno em `_sets`. O set da Terra
		// que as secoes de cima deixaram ja foi gasto, espalhado e remexido a mao -- nao e nascimento de nada.
		_invocacoes.Clear();
		_sets.RemoveAll(s => !s.Eterno);
		_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
		foreach (Esfera e in _esferas) e.Portador = 0;

		// O RADAR NA MOCHILA: e por ele e pelo `db_ver` que a cena le o que o SERVIDOR responde, nas palavras que
		// o jogador le -- o outro lado da comparacao com a tela.
		if (pl.Mochila.Quantos(Jandirus.Core.Items.CatalogoDeItens.Radar) <= 0)
			pl.Mochila.Guardar(Jandirus.Core.Items.CatalogoDeItens.Radar);

		// ------------------------------------------------------------------ NA TERRA: o set de jogador
		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));

		SetDeEsferas? DaTerra() => _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);

		// A bancada tira a estatua anterior na mao: o que se mede e o NASCIMENTO, e a derrubada (`db_derrubar`)
		// nao e desta cena.
		SetDeEsferas? Erguer()
		{
			if (DaTerra() is { } velho)
			{
				_esferas.RemoveAll(e => e.Set == velho.Id);
				_sets.Remove(velho);
			}
			ErguerEstatua(pl, "");
			return DaTerra();
		}

		ChecarAVirada(Checa, "set de jogador recem-erguido (`db_estatua`)", NascerEAcordarNaTela(pl, Erguer));

		// `db_refazer` num set ja acordado: as sete nascem de novo, com a espera de nascimento e a contagem em zero.
		ChecarAVirada(Checa, "set refeito pelo criador (`db_refazer`)",
			NascerEAcordarNaTela(pl, () => { RefazerOSet(pl, "2"); return DaTerra(); }));

		// O CONTROLE: o set que gastou tudo o que tinha (o funil de producao conta, apaga e espalha). Este sempre
		// avisou ao acordar -- e a cena que mostra que a escuta do fio enxerga um aviso quando ele sai. Sem ela,
		// "nao chegou pacote" nas cenas de cima podia ser uma escuta surda.
		ChecarAVirada(Checa, "CONTROLE: set que gastou os pedidos (`ContarUmDesejo`)",
			NascerEAcordarNaTela(pl, () =>
			{
				if (DaTerra() is not { } s) return null;
				s.Desejos = 1;
				s.Pedidos = 0;
				ContarUmDesejo(s);
				return s;
			}));

		MutacaoDeEsfera(Checa,
			"set recem-nascido que acorda: a tela de quem esta no planeta RECEBE a estatua e as sete acesas",
			"so avisa a zona o set que tem pedido gasto pra zerar -- o que este port fez ate 2026-10-09",
			() => NascerEAcordarNaTela(pl, Erguer) is var x && x.NasceApagado && x.OServidorAcordou && x.ATelaAcendeu,
			() => AcordarCaladoDeTeste = true,
			() => AcordarCaladoDeTeste = false);

		MutacaoDeEsfera(Checa,
			$"o aviso da virada sai UMA vez: um pacote no tique em que o set acorda, nenhum nos {TiquesDepoisDaVirada} seguintes",
			"o trinco da virada solto antes de cada tique -- o pacote por segundo que o `Pedidos != 0` evitava",
			() => NascerEAcordarNaTela(pl, Erguer) is var x && x.PacotesNaVirada == 1 && x.PacotesDepois == 0,
			() => _trincoSolto = true,
			() => _trincoSolto = false);

		// ------------------------------------------------------------------ EM NAMEK: o Porunga
		MoveToZone(pl.Id, namek, PontoDeNascimento(namek));

		// O `ErguerOSetEterno` e por onde o Porunga nasce nos tres casos de producao: o primeiro boot de um mundo, a
		// limpeza total e a restauracao do planeta. O primeiro tique dele corta a espera de nascimento (0,4 ano)
		// pro teto do zelador (0,1) -- e a espera que a cena atravessa e a que sobra depois do corte.
		ChecarAVirada(Checa, "Porunga de um mundo novo (`ErguerOSetEterno`)",
			NascerEAcordarNaTela(pl, () =>
			{
				_esferas.RemoveAll(e => e.Set == Esferas.IdDoSetEterno);
				_sets.RemoveAll(s => s.Eterno);
				ErguerOSetEterno();
				return _sets.Find(s => s.Eterno);
			}));

		// O ZELADOR REFAZ AS SETE do eterno ja acordado porque uma sumiu (`eternal_maintain`, `Dragonballs.dm:408-412`):
		// o nascimento aqui e do proprio tique de producao, e nao de verbo nenhum.
		ChecarAVirada(Checa, "Porunga refeito pelo zelador (uma das sete sumiu)",
			NascerEAcordarNaTela(pl, () =>
			{
				if (_sets.Find(x => x.Eterno) is not { } s || _esferas.Find(x => x.Set == s.Id) is not { } sumida)
					return null;
				_esferas.Remove(sumida);
				TickDasEsferas();
				return s;
			}));
	}

	/// <summary>Quantos tiques a cena ainda roda DEPOIS de o set acordar, pra ver se o aviso se repete.</summary>
	private const int TiquesDepoisDaVirada = 5;

	/// <summary>
	/// O QUE A TELA DE QUEM ESTA PARADO NO PLANETA RECEBEU, do nascimento de um set ate bem depois de ele acordar --
	/// lido do fio (<see cref="EscutaDoFioDasEsferas"/>), ao lado do que o servidor responde a quem pergunta.
	/// </summary>
	private readonly record struct ViradaNaTela(
		bool Nasceu, int PacotesAoNascer, int CoisasAoNascer, int ApagadasAoNascer, double Espera,
		bool DbVerAcordadas, int SinaisNoRadar, int PacotesNaVirada, int PacotesDepois,
		int CoisasNaTela, int ApagadasNaTela, string EstadosNaTela)
	{
		/// <summary>O retrato de nascimento chegou, com a estatua e as sete, todas apagadas.</summary>
		public bool NasceApagado =>
			Nasceu && PacotesAoNascer >= 1 && CoisasAoNascer == 1 + Esferas.Total && ApagadasAoNascer == CoisasAoNascer;

		/// <summary>Passada a espera, o servidor responde acordado pelos dois verbos que o jogador tem pra perguntar.</summary>
		public bool OServidorAcordou => DbVerAcordadas && SinaisNoRadar == Esferas.Total;

		/// <summary>O ultimo retrato que a tela tem mostra a estatua e as sete, nenhuma apagada.</summary>
		public bool ATelaAcendeu => CoisasNaTela == 1 + Esferas.Total && ApagadasNaTela == 0;
	}

	/// <summary>
	/// UM SET NASCE, ATRAVESSA A ESPERA E ACORDA com o testador parado no planeta dele. `nascer` e a porta de
	/// producao que faz o set nascer (e devolve o set); o resto e o tique de producao e o relogio do mundo.
	///
	/// O RELOGIO ANDA, e nao o carimbo do set: mexer no `AtivoEm` seria trocar a regra da espera por outra pra
	/// medir o aviso dela. Quem chama devolve o relogio (o `finally` da bancada).
	/// </summary>
	private ViradaNaTela NascerEAcordarNaTela(ServerPlayer pl, Func<SetDeEsferas?> nascer)
	{
		var fio = new List<(int Para, byte[] Fio)>();
		List<string>? avisosAntes = EscutaDeAvisos;
		EscutaDoFioDasEsferas = fio;
		try
		{
			if (nascer() is not { } s) return default;

			// O PRIMEIRO SEGUNDO DE VIDA, ainda apagado. No eterno e aqui que o zelador corta a espera.
			TickDasEsferas();
			int aoNascer = fio.Count(p => p.Para == pl.Id);
			List<CoisaNoFio> telaAoNascer = TelaDoSet(fio, pl.Id, s.Id);

			double espera = Math.Max(0, s.AtivoEm - TempoDoMundo);
			_adiantoDoCeu += espera + 5;

			TickDasEsferas();   // o tique da virada
			int naVirada = fio.Count(p => p.Para == pl.Id) - aoNascer;

			for (int i = 0; i < TiquesDepoisDaVirada; i++)
			{
				if (_trincoSolto) s.VistoAcordado = false;
				TickDasEsferas();
			}
			int depois = fio.Count(p => p.Para == pl.Id) - aoNascer - naVirada;
			List<CoisaNoFio> tela = TelaDoSet(fio, pl.Id, s.Id);

			// O OUTRO LADO: o que o servidor responde, pelos verbos de producao e nas palavras que o jogador le.
			// Nenhum dos dois manda `S2C.Esferas` -- a tela de cima ja foi lida, e eles nao a mudariam.
			EscutaDeAvisos = [];
			VerAsEsferas(pl);
			bool dbVer = EscutaDeAvisos.Any(t => t.Contains("ACORDADAS"));
			EscutaDeAvisos.Clear();
			UsarORadar(pl);
			int radar = EscutaDeAvisos.Count(t => t.Contains("estrela"));

			return new ViradaNaTela(true, aoNascer, telaAoNascer.Count, telaAoNascer.Count(c => c.Apagada), espera,
				dbVer, radar, naVirada, depois, tela.Count, tela.Count(c => c.Apagada), EstadosDasEsferas(tela));
		}
		finally
		{
			EscutaDoFioDasEsferas = null;
			EscutaDeAvisos = avisosAntes;
		}
	}

	/// <summary>As tres afirmacoes de uma cena da secao 14, com a linha de medida na frente.</summary>
	private static void ChecarAVirada(ChecagemDeEsfera Checa, string porta, ViradaNaTela m)
	{
		string servidor = $"db_ver {(m.DbVerAcordadas ? "ACORDADAS" : "apagadas")}, radar com {m.SinaisNoRadar} sinal(is)";
		string naTela = $"{m.ApagadasNaTela} de {m.CoisasNaTela} coisas apagadas (esferas: {m.EstadosNaTela})";

		GD.Print($"  [medido] {porta}: nasce com {m.PacotesAoNascer} pacote(s) pra tela, {m.ApagadasAoNascer} de "
			   + $"{m.CoisasAoNascer} coisas apagadas | espera de {m.Espera / 3600.0:0.00} h | passada a espera o servidor "
			   + $"responde: {servidor} | pra tela: {m.PacotesNaVirada} pacote(s) no tique da virada, {m.PacotesDepois} nos "
			   + $"{TiquesDepoisDaVirada} seguintes | a tela ficou com {naTela}");

		Checa($"{porta}: NASCE APAGADO na tela de quem esta no planeta (a estatua e as {Esferas.Total}, lidas do fio)",
			  m.NasceApagado,
			  !m.Nasceu ? "o set nao nasceu" : $"{m.PacotesAoNascer} pacote(s), {m.ApagadasAoNascer} de {m.CoisasAoNascer} apagadas");
		Checa("...e quando a espera vence a tela RECEBE a virada: um pacote, com a estatua e as sete acesas",
			  m.OServidorAcordou && m.PacotesNaVirada == 1 && m.ATelaAcendeu,
			  $"o servidor responde: {servidor}; pra tela foram {m.PacotesNaVirada} pacote(s), e ela ficou com {naTela}");
		Checa($"...e UMA vez: os {TiquesDepoisDaVirada} tiques seguintes nao mandam mais nada",
			  m.Nasceu && m.PacotesDepois == 0, $"{m.PacotesDepois} pacote(s) a mais");
	}

	/// <summary>UMA COISA DO `S2C.Esferas` COMO ELA VIAJA: o que a tela usa pra decidir o desenho.</summary>
	private readonly record struct CoisaNoFio(int Id, Protocol.CoisaDeEsfera Tipo, int Numero, bool Apagada);

	/// <summary>
	/// LE UM `S2C.Esferas` DO FIO, campo a campo na ordem do leitor do cliente (`GameClient`, ramo `S2C.Esferas`).
	/// A posicao e a folha sao lidas e largadas: quem decide apagada ou acesa e o bit.
	/// </summary>
	private static List<CoisaNoFio> LerOFioDasEsferas(byte[] fio)
	{
		var r = new LiteNetLib.Utils.NetDataReader(fio);
		r.GetByte();   // o opcode
		int n = r.GetUShort();
		var coisas = new List<CoisaNoFio>(n);
		for (int i = 0; i < n; i++)
		{
			int id = r.GetInt();
			var tipo = (Protocol.CoisaDeEsfera)r.GetByte();
			int numero = r.GetByte();
			r.GetFloat();
			r.GetFloat();
			bool apagada = r.GetBool();
			r.GetString(16);
			coisas.Add(new CoisaNoFio(id, tipo, numero, apagada));
		}
		return coisas;
	}

	/// <summary>
	/// A TELA DE UM JOGADOR, recortada num set: o ULTIMO `S2C.Esferas` que saiu pra ele (o cliente troca a lista
	/// inteira a cada pacote), so com a estatua e as esferas daquele set -- pelo id de tela `set*10 + n` do
	/// <see cref="MandarEsferas"/>. Vazia se nao chegou pacote nenhum.
	/// </summary>
	private static List<CoisaNoFio> TelaDoSet(List<(int Para, byte[] Fio)> fio, int jogador, int set)
	{
		int ultimo = fio.FindLastIndex(p => p.Para == jogador);
		if (ultimo < 0) return [];

		return [.. LerOFioDasEsferas(fio[ultimo].Fio).Where(c =>
			(c.Tipo == Protocol.CoisaDeEsfera.Estatua && c.Id == set * 10)
			|| (c.Tipo == Protocol.CoisaDeEsfera.Esfera && c.Numero >= 1 && c.Numero <= Esferas.Total
				&& c.Id == set * 10 + c.Numero))];
	}

	/// <summary>
	/// O `icon_state` de cada esfera de uma tela, pela MESMA funcao que o cliente chama pra escolher a animacao
	/// (<see cref="Esferas.EstadoDoSprite"/>, via `EsferaDesenhada.FolhaDe`): "inactive x7" ou "1,2,3,4,5,6,7".
	/// </summary>
	private static string EstadosDasEsferas(List<CoisaNoFio> tela) => string.Join(",",
		tela.Where(c => c.Tipo == Protocol.CoisaDeEsfera.Esfera).OrderBy(c => c.Numero)
			.Select(c => Esferas.EstadoDoSprite(c.Numero, c.Apagada))
			.GroupBy(e => e).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key));

	/// <summary>
	/// O `Mutacao` da `--provateste`, na mesma forma e pelo mesmo motivo: **uma checagem que nunca
	/// soube ficar vermelha e indistinguivel de checagem nenhuma**.
	///
	/// O `finally` em volta do `consertar` nao e cerimonia -- um criterio que le o mundo pode explodir
	/// num mundo estragado, e o mundo tem que voltar antes de a excecao subir.
	/// </summary>
	private static void MutacaoDeEsfera(ChecagemDeEsfera Checa, string oQue, string oDefeito,
										Func<bool> criterio, Action estragar, Action consertar)
	{
		Checa(oQue, criterio(),
			  "o criterio ja reprova ANTES do defeito -- nao ha nada sendo injetado aqui");

		bool caiu;
		try
		{
			estragar();
			caiu = !criterio();
		}
		finally { consertar(); }

		Checa($"   DEFEITO INJETADO ({oDefeito}): o MESMO criterio REPROVA", caiu,
			  "a checagem de cima e decoracao -- ela nao sabe ficar vermelha");
		Checa("   ...e desfeito o defeito ele volta a passar (era a causa, e nao um estrago que ficou)",
			  criterio());
	}
}
