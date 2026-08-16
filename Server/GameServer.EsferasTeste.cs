using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

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
/// ================================================================================
///
/// ============================ AS QUATRO FAMILIAS COM DEFEITO INJETADO ============================
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
		}
		finally
		{
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
