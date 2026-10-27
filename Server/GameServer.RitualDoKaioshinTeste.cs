using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--ritualkaiteste` -- O RITUAL MISTICO DO VELHO KAIOHSHIN E O POTENTIAL UNLEASHED (dono, 2026-10-23). O pedido e as decisoes
/// estao no cabecalho de `Core/Ranks/RitualDoKaioshin.cs`; a regra, no `GameServer.RitualDoKaioshin.cs` e no
/// `GameServer.ReconciliarODomDoMistico`. Corpos forjados (sem tela), e tudo pelas portas de producao: o canal de habilidade
/// (`UsarHabilidade`), a resposta do cartao (`ComandoDeCargo`, os mesmos verbos do Unlock), a reconciliacao do cargo, o tique dos
/// rituais e a tecla C (`Transformar`). O CODIGO na frente de cada rotulo e o que o ciclo de contraprova procura:
///
///   A. O NOME: a forma e a skill se chamam Potential Unleashed, e a concessao o diz.
///   B. O KIT: o Kaioshin nao recebe mais a skill nem a forma; o painel do cargo anuncia o ritual; o botao e do trono.
///   C. A MIGRACAO: o Kaioshin de antes da regra perde a skill e a forma do cargo (contra-exemplo `MisticoDoCargoFicaDeTeste`); a copia
///      ENSINADA fica; a forma sem skill (o desejo, o ritual) NAO some no tique seguinte (contra-exemplo `FormaSegueASkillDeTeste`).
///   D. A OFERTA: so o Kaioshin, so com alguem na frente, so a quem o ritual ainda da alguma coisa; o cartao e os botoes do Unlock; a
///      recusa; um convite por alvo.
///   E. O RITUAL: o aceite trava os dois (contra-exemplo `RitualSemTravaDeTeste`), o chat conta os minutos, o fim desperta o potencial e
///      abre a forma (contra-exemplo `RitualSemDomDeTeste`), a forma fica, e o C leva a ela mesmo sem escada de raca (contra-exemplo
///      `ConcedidaSemEscadaDeTeste`).
///   F. O DIA: um ritual por dia real (contra-exemplo `RitualSemDiaDeTeste`), e o interrompido nao gasta (contra-exemplo
///      `DiaDoRitualNoComecoDeTeste`).
///   G. O QUE QUEBRA: o botao (dos dois lados), sair do lugar (contra-exemplo `RitualNaoQuebraDeTeste`), um golpe, a queda, o trono,
///      sair do mundo e trocar de zona.
///   H. QUEM JA DESPERTOU ganha so a forma; e um Kaioshin faz o ritual em OUTRO Kaioshin.
///
/// CADA FAMILIA COMECA DE CORPOS NOVOS (`Par`): o defeito de uma nao arrasta as seguintes, e a contraprova por mutacao derruba so a
/// afirmacao da linha que tirou. O relogio anda pelo `AdiantoDoRelogioDeTeste` (o dia e os dez minutos sao de PAREDE), e so pra frente.
///
///     Godot --headless --path . --server --port 10980 --ritualkaiteste
/// </summary>
public sealed partial class GameServer
{
	private bool _ritualKaiJaRodou;
	private int _rkOk, _rkFalhou;

	/// <summary>O comeco dos ids dos corpos desta bancada -- fora da faixa dos de jogo e das outras bancadas.</summary>
	private const int IdBaseDoRitualDeTeste = 988_600;

	private void AfirmarRk(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _rkOk++; GD.Print($"[ritualkai]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _rkFalhou++; GD.PrintErr($"[ritualkai]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoRitualDoKaioshin()
	{
		if (_ritualKaiJaRodou) return;
		_ritualKaiJaRodou = true;
		_rkOk = _rkFalhou = 0;
		GD.Print("[ritualkai] ================ O RITUAL MISTICO DO VELHO KAIOHSHIN E O POTENTIAL UNLEASHED (dono, 2026-10-23) ================");

		var forjados = new List<ServerPlayer>();
		var tronosAntes = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		List<(int Quem, string Id, long Ms)>? efeitosAntes = EscutaDeEfeitos;
		try
		{
			RoteiroDoRitual(forjados);
		}
		catch (Exception e)
		{
			AfirmarRk("Z0. o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
		finally
		{
			RitualSemTravaDeTeste = RitualSemDiaDeTeste = DiaDoRitualNoComecoDeTeste = RitualNaoQuebraDeTeste = RitualSemDomDeTeste = false;
			FormaSegueASkillDeTeste = MisticoDoCargoFicaDeTeste = ConcedidaSemEscadaDeTeste = false;
			EscutaDeEfeitos = efeitosAntes;
			_rituais.Clear();
			foreach (ServerPlayer c in forjados)
			{
				_ofertasDeRitual.Remove(c.Conta);
				_ofertasDePotencialG11.Remove(c.Conta);
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			}
			_tronos.Clear();
			foreach ((string vaga, string conta) in tronosAntes) _tronos[vaga] = conta;
			GD.Print($"[ritualkai] ================ {_rkOk} OK, {_rkFalhou} FALHA(S) ================");
		}
	}

	private void RoteiroDoRitual(List<ServerPlayer> forjados)
	{
		const float T = ZoneCollision.TileSize;
		ZoneKey terra = SpawnZone;
		string nomeDaForma = Catalogo.Def(Catalogo.IdMistico)?.Nome ?? "";
		int proximo = 0;

		// UM CORPO DE GENTE: conta e vaga (a assinatura -- `EhPessoa` -- e a CONTA dos convites), e a ficha pela porta de nascimento
		// (sem ela o `CapCheck` do despertar devolve zero e "o potencial despertou" nao se mede).
		ServerPlayer Gente(string nome, string raca, Vec2 pos, Facing frente)
		{
			int i = proximo++;
			var c = new ServerPlayer
			{
				Id = IdBaseDoRitualDeTeste + i, Peer = null, Name = nome, Race = raca, Genero = "Male", Idade = 30,
				Zone = terra, Pos = pos, Conta = $"bancada_ritualkai_{i}", Slot = 0,
				Ficha = _racas != null
					? Jandirus.Core.Races.Birth.Nascer(_racas, raca, "", new Random(4100 + i), nome)
					: new Fighter { Race = raca, BP = 100_000 },
				Livro = new SkillBook(),
			};
			c.Facing = frente;
			PorNoMundo(c);
			c.Ficha.Ki = c.Ficha.MaxKi;
			c.Ficha.Tick(agoraMs: NowMs());
			forjados.Add(c);
			return c;
		}

		// O PAR DE CADA FAMILIA: o Kaioshin (no trono, olhando pro leste, com o alvo marcado) e o alvo no tile da frente, olhando pra ele.
		// Cada par numa faixa propria do chao -- e os de familias passadas saem do mundo antes.
		int faixa = 0;
		(ServerPlayer K, ServerPlayer A) Par(string racaDoAlvo = "Human")
		{
			foreach (ServerPlayer velho in forjados.ToList())
				if (_players.ContainsKey(velho.Id)) RemoverNpc(velho);
			_rituais.Clear();
			Vec2 p = SpawnPos + new Vec2(-8 * T + T / 2, (-9 + 2 * (faixa++ % 6)) * T);
			ServerPlayer k = Gente("Kaioshin", "Kai", p, Facing.East);
			ServerPlayer a = Gente("Alvo", racaDoAlvo, p + new Vec2(T, 0), Facing.West);
			_tronos.Clear();
			_tronos[RitualDoKaioshin.Cargo] = k.Conta;
			k.AlvoId = a.Id;
			return (k, a);
		}

		bool Ve(ServerPlayer quem, string chave) =>
			RequisitosDosVerbos.Linha(chave) is { } l && l.Requisito.Atende(new PosseDoServidor(this, quem));
		string BotaoDoRitual = RequisitosDosVerbos.PrefixoDeHabilidade + RitualDoKaioshin.Verbo;
		List<string> Apertar(ServerPlayer quem) => Ouvir(() => UsarHabilidade(quem, RitualDoKaioshin.Verbo));
		List<string> Responder(ServerPlayer quem, bool sim) => Ouvir(() => ComandoDeCargo(quem, sim ? "potencial_aceitar" : "potencial_recusar", ""));
		void Andar(long ms) => AdiantoDoRelogioDeTeste += ms;
		List<string> Tique() => Ouvir(TickDosRituais);
		bool EmRitual(ServerPlayer quem) => RitualDe(quem) != null;
		bool TemAForma(ServerPlayer quem) => quem.Forma.Despertou(Catalogo.IdMistico);
		bool Oferecido(ServerPlayer alvo) => _ofertasDeRitual.ContainsKey(alvo.Conta);
		string CartaoDe(ServerPlayer quem)
		{
			var lista = new List<PedidoNaTela>();
			PedidosDe(quem, lista);
			return string.Join(" | ", lista.Where(p => p.Fato == Fatos.OfertaDePotencial).Select(p => p.Texto));
		}
		// O RITUAL INTEIRO ATE O ACEITE, pelas portas de producao: o aperto do Kaioshin e o "aceitar" do cartao. Devolve o que o servidor
		// disse nos dois gestos -- e quem chama NAO o embrulha num `Ouvir`: o ouvido e um so, e o de dentro calaria o de fora.
		List<string> Comecar(ServerPlayer k, ServerPlayer a)
		{
			List<string> falas = Apertar(k);
			falas.AddRange(Responder(a, sim: true));
			return falas;
		}

		// ============================================================ A. O NOME
		AfirmarRk("A1. O NOME: a forma do Mistico se chama \"Potential Unleashed\" no catalogo -- o nome que toda tela le (`Catalogo.NomeDe`)",
				  nomeDaForma == "Potential Unleashed", nomeDaForma);
		AfirmarRk("A2. ...e a skill que era a forma (`kai/Mystic`) tambem, no catalogo de skills",
				  _skills?.Get(RitualDoKaioshin.SkillDoMistico)?.Nome == "Potential Unleashed", _skills?.Get(RitualDoKaioshin.SkillDoMistico)?.Nome ?? "");
		{
			(ServerPlayer k, ServerPlayer a) = Par();
			List<string> concedeu = Ouvir(() => ConcederMistico(a, "bancada"));
			AfirmarRk("A3. ...e quem a ganha OUVE o nome novo (a frase da concessao, a mesma do ritual e do desejo)",
					  Disse(concedeu, "o Potential Unleashed é seu") && !Disse(concedeu, "Mistico"), Ultimos(concedeu));
		}

		// ============================================================ B. O KIT
		{
			string[] kit = DadivaDeCargo.De(RitualDoKaioshin.Cargo);
			AfirmarRk("B1. O KIT DO KAIOSHIN NAO TEM MAIS A SKILL DO MISTICO (`kai/Mystic`) -- o dono trocou pelo ritual",
					  kit.Length > 5 && !kit.Contains(RitualDoKaioshin.SkillDoMistico, StringComparer.OrdinalIgnoreCase), string.Join(", ", kit));
			AfirmarRk("B2. ...e o Grand Kai, o Demon Lord e os Ancioes CONTINUAM com o Unlock Potential deles (o dono: \"fica como esta\")",
					  new[] { "grandkai", "demonlord", "elder", "northelder" }.All(c => DadivaDeCargo.De(c).Contains("/datum/skill/rank/Unlock_Potential")));

			(ServerPlayer k, ServerPlayer a) = Par();
			List<string> coroado = Ouvir(() => ReconciliarDadiva(k));
			AfirmarRk("B3. **O CARGO NAO DA MAIS A FORMA**: o Kaioshin coroado nao recebe a skill nem o Potential Unleashed -- e o cargo nao o anuncia",
					  !k.Livro.Sabe(RitualDoKaioshin.SkillDoMistico) && !TemAForma(k) && !Disse(coroado, "Potential Unleashed") && !Disse(coroado, "Mystic"),
					  $"skill {k.Livro.Sabe(RitualDoKaioshin.SkillDoMistico)}, forma {TemAForma(k)} | {Ultimos(coroado)}");
			string painel = OQueOCargoEntrega(RitualDoKaioshin.Cargo);
			AfirmarRk("B4. O PAINEL DO CARGO anuncia o Ritual Mistico do Velho Kaiohshin (e nao mais o Mistico)",
					  painel.StartsWith(RitualDoKaioshin.Nome, StringComparison.Ordinal) && !painel.Contains("Potential Unleashed") && !painel.Contains("Mystic")
					  && painel.Length <= 400, painel);
			AfirmarRk("B5. O BOTAO DO RITUAL e do trono: o Kaioshin o ve, e quem nao tem o trono nao",
					  Ve(k, BotaoDoRitual) && !Ve(a, BotaoDoRitual));
			RankDef? r = Cargos.Get(RitualDoKaioshin.Cargo);
			AfirmarRk("B6. ...e o texto do cargo diz o ritual, e nao o Mistico (a descricao cabe no fio: ate 200 letras)",
					  r != null && r.Desc.Contains(RitualDoKaioshin.Nome) && !r.Desc.Contains("Místico sem prazo") && r.Desc.Length <= 200, r?.Desc ?? "");
		}

		// ============================================================ C. A MIGRACAO
		{
			// C1. O KAIOSHIN DE ANTES DA REGRA: a skill que o cargo deu, a forma derivada dela -- e ele DENTRO da forma.
			(ServerPlayer k, ServerPlayer _) = Par();
			void ComoAntes()
			{
				k.Livro.Dar(RitualDoKaioshin.SkillDoMistico);
				k.Forma.Liberar(Catalogo.IdMistico);
				k.Forma.Entrar(Catalogo.IdMistico);
			}
			ComoAntes();
			MisticoDoCargoFicaDeTeste = true;
			try { ReconciliarDadiva(k); }
			finally { MisticoDoCargoFicaDeTeste = false; }
			AfirmarRk("C1x. (defeito injetado: a skill do cargo fica, e a forma com ela) a regua do C1 REPROVA -- o Kaioshin de antes continua com o Potential Unleashed sem ritual",
					  k.Livro.Sabe(RitualDoKaioshin.SkillDoMistico) && TemAForma(k));
			k.Forma.Entrar(Catalogo.IdBase);
			k.Livro.Esquecer(RitualDoKaioshin.SkillDoMistico);
			k.Forma.Liberadas.Remove(Catalogo.Rede(Catalogo.IdMistico));

			ComoAntes();
			List<string> perdeu = Ouvir(() => ReconciliarDadiva(k));
			AfirmarRk("C1. **O KAIOSHIN CUJA FORMA VINHA SO DO CARGO A PERDE**: a skill sai do livro, a forma fecha, ele volta pra base -- e o jogo diz por que",
					  !k.Livro.Sabe(RitualDoKaioshin.SkillDoMistico) && !TemAForma(k) && k.Forma.Atual == Catalogo.IdBase
					  && Disse(perdeu, "não abre mais o Potential Unleashed") && Disse(perdeu, RitualDoKaioshin.Nome),
					  $"skill {k.Livro.Sabe(RitualDoKaioshin.SkillDoMistico)}, forma {TemAForma(k)}, atual {k.Forma.Atual} | {Ultimos(perdeu)}");

			// C2. A COPIA ENSINADA (a licao de um Kaioshin, antes da regra): "quem tem fica".
			(ServerPlayer _, ServerPlayer aluno) = Par();
			aluno.Livro.DarComoEnsinada(RitualDoKaioshin.SkillDoMistico);
			aluno.Forma.Liberar(Catalogo.IdMistico);
			for (int i = 0; i < 3; i++) ReconciliarDadiva(aluno);
			AfirmarRk("C2. QUEM APRENDEU O MISTICO de um Kaioshin (a copia ensinada) FICA com a forma -- e com a skill (o dono: \"quem tem fica\")",
					  TemAForma(aluno) && aluno.Livro.Sabe(RitualDoKaioshin.SkillDoMistico));

			// C3. A FORMA SEM A SKILL -- a do desejo e a do ritual: o defeito de antes a tirava no tique seguinte.
			(ServerPlayer _, ServerPlayer doDesejo) = Par();
			doDesejo.Forma.Liberar(Catalogo.IdMistico);
			FormaSegueASkillDeTeste = true;
			try { ReconciliarDadiva(doDesejo); }
			finally { FormaSegueASkillDeTeste = false; }
			AfirmarRk("C3x. (defeito injetado: a forma segue a skill, o jogo de antes) a regua do C3 REPROVA -- a forma sem a skill some no tique seguinte",
					  !TemAForma(doDesejo));
			doDesejo.Forma.Liberar(Catalogo.IdMistico);
			for (int i = 0; i < 3; i++) ReconciliarDadiva(doDesejo);
			AfirmarRk("C3. **A FORMA SEM A SKILL NAO SOME MAIS** (o desejo, o ritual): tres reconciliacoes do cargo depois, ela continua -- e um direito gravado",
					  TemAForma(doDesejo));
		}

		// ============================================================ D. A OFERTA
		{
			(ServerPlayer k, ServerPlayer a) = Par();
			List<string> doComum = Apertar(a);
			AfirmarRk("D1. QUEM NAO E O KAIOSHIN aperta o ritual e ouve \"só o Kaioshin\" -- e nenhum convite sai",
					  Disse(doComum, "só o Kaioshin") && !Oferecido(k) && !Oferecido(a), Ultimos(doComum));

			Facing antes = k.Facing;
			k.Facing = Facing.West;   // de costas pro alvo
			List<string> deCostas = Apertar(k);
			k.Facing = antes;
			AfirmarRk("D2. O KAIOSHIN DE COSTAS pro alvo nao oferece nada: o ritual e com quem esta NA FRENTE",
					  Disse(deCostas, "fique na frente") && !Oferecido(a), Ultimos(deCostas));

			List<string> ofereceu = Apertar(k);
			string cartao = CartaoDe(a);
			AfirmarRk("D3. **O KAIOSHIN OFERECE O RITUAL** a quem esta na frente: o convite chega no cartao do alvo (o mesmo do Unlock Potential), com o nome do ritual",
					  Oferecido(a) && cartao.Contains(RitualDoKaioshin.Nome) && cartao.Contains(k.Name) && Disse(ofereceu, "você oferece"),
					  $"cartao: {cartao} | {Ultimos(ofereceu)}");
			AfirmarRk("D4. ...e o alvo ve os botoes Accept / Decline Potential (o fato do cartao), e o Kaioshin nao",
					  (FatosDe(a) & Fatos.OfertaDePotencial) != 0 && Ve(a, "verbo:potencial_aceitar") && Ve(a, "verbo:potencial_recusar")
					  && !Ve(k, "verbo:potencial_aceitar"));
			AfirmarRk("D5. ...e nada muda ate ele responder: ninguem preso, nenhum ritual, nada despertou",
					  !EmRitual(k) && !EmRitual(a) && a.Ficha.unlockPotential < 1 && !TemAForma(a) && PodeMexerOCorpo(a));

			List<string> recusa = Responder(a, sim: false);
			AfirmarRk("D6. RECUSAR (o \"Recusar\" do cartao) desfaz o convite: nenhum ritual, e o Kaioshin ouve",
					  !Oferecido(a) && !EmRitual(a) && Disse(recusa, "recusou o ritual") && CartaoDe(a).Length == 0, Ultimos(recusa));

			// UM CONVITE POR ALVO: o Unlock de um Grand Kai e o ritual se substituem -- o mais novo fica.
			Apertar(k);
			ServerPlayer grand = Gente("GrandKai", "Kai", a.Pos + new Vec2(0, T), Facing.North);
			grand.AlvoId = a.Id;
			OferecerPotencialG11(grand);
			AfirmarRk("D7. UM CONVITE POR ALVO: o Unlock de um Grand Kai depois do ritual SUBSTITUI o convite do ritual (o cartao e um so)",
					  !Oferecido(a) && _ofertasDePotencialG11.ContainsKey(a.Conta) && !CartaoDe(a).Contains(RitualDoKaioshin.Nome), CartaoDe(a));
			Apertar(k);
			AfirmarRk("D7b. ...e o ritual depois do Unlock substitui o Unlock", Oferecido(a) && !_ofertasDePotencialG11.ContainsKey(a.Conta), CartaoDe(a));
			Responder(a, sim: false);
			RemoverNpc(grand);

			// (2026-10-26) O RITUAL E UMA VEZ POR PERSONAGEM (o dono): quem ja tem o potencial desperto e a forma POR OUTRO CAMINHO ainda o
			// recebe (ele solta o banco 2x) -- e quem ja o recebeu nao recebe o convite de novo.
			a.Ficha.unlockPotential = 1;
			a.Forma.Liberar(Catalogo.IdMistico);
			List<string> comTudo = Apertar(k);
			AfirmarRk("D8. QUEM JA TEM O POTENCIAL DESPERTO E A FORMA (por outro caminho) ainda recebe o convite: o ritual solta o banco 2x",
					  Oferecido(a), Ultimos(comTudo));
			Responder(a, sim: false);
			a.Ficha.ritualDoKaioshinRecebido = true;
			List<string> semNada = Apertar(k);
			AfirmarRk("D8b. **QUEM JA RECEBEU O RITUAL nao recebe o convite** (o dono: \"o ritual do kaioshin so pode ser feito 1 vez por personagem\"): o Kaioshin ouve por que",
					  !Oferecido(a) && Disse(semNada, "uma vez só por personagem"), Ultimos(semNada));
		}

		// ============================================================ E. O RITUAL
		{
			// O ALVO E UM KAI SEM KI DIVINO -- sem escada de raca nenhuma: e quem prova que o C leva a forma concedida (E6).
			(ServerPlayer k, ServerPlayer a) = Par(racaDoAlvo: "Kai");
			ServerPlayer outro = Gente("Testemunha", "Human", a.Pos + new Vec2(0, 3 * T), Facing.North);
			AfirmarRk("E0. (montagem) o alvo e um Kai sem escada nenhuma aberta, com o potencial adormecido e sem a forma",
					  Catalogo.LinhasAbertas(Perfil(a)).Count == 0 && a.Ficha.unlockPotential < 1 && !TemAForma(a),
					  $"linhas {Catalogo.LinhasAbertas(Perfil(a)).Count}, potencial {a.Ficha.unlockPotential}");

			var efeitos = new List<(int Quem, string Id, long Ms)>();
			EscutaDeEfeitos = efeitos;
			List<string> comecou = Comecar(k, a);
			AfirmarRk("E1. **O ACEITE COMECA O RITUAL**: os dois estao nele, e os dois ouvem que comecou",
					  EmRitual(k) && EmRitual(a) && comecou.Count(f => f.Contains("o ritual começou")) == 2, Ultimos(comecou));
			AfirmarRk("E2. **NENHUM DOS DOIS SE MOVE** (o funil de vetor `PodeMexerOCorpo`) -- e quem esta de fora anda",
					  !PodeMexerOCorpo(k) && !PodeMexerOCorpo(a) && PodeMexerOCorpo(outro));
			AfirmarRk("E3. ...e o cliente dos dois e avisado (o efeito `ritual_kaioshin` aceso), pra nem prever o passo",
					  efeitos.Any(e => e.Quem == k.Id && e.Id == RitualDoKaioshin.EfeitoDaTrava && e.Ms < 0)
					  && efeitos.Any(e => e.Quem == a.Id && e.Id == RitualDoKaioshin.EfeitoDaTrava && e.Ms < 0));
			AfirmarRk("E4. ...e o BOTAO do ritual passa a ser do alvo tambem (o fato `RitualDoKaioshin`) -- e o que o interrompe",
					  (FatosDe(a) & Fatos.RitualDoKaioshin) != 0 && Ve(a, BotaoDoRitual) && !Ve(outro, BotaoDoRitual));
			RitualSemTravaDeTeste = true;
			try
			{
				AfirmarRk("E2x. (defeito injetado: o ritual sem trava) a regua do E2 REPROVA -- os dois andam no meio do ritual",
						  PodeMexerOCorpo(k) && PodeMexerOCorpo(a));
			}
			finally { RitualSemTravaDeTeste = false; }

			Andar(3 * RitualDoKaioshin.AvisoACadaMs + 1_000);
			List<string> tres = Tique();
			AfirmarRk("E5. O CHAT CONTA OS MINUTOS: aos tres minutos os dois ouvem \"3 de 10\" -- e o ritual continua, sem nada despertado ainda",
					  Disse(tres, "3 de 10 minutos") && tres.Count(f => f.Contains("3 de 10 minutos")) == 2 && EmRitual(a) && a.Ficha.unlockPotential < 1,
					  Ultimos(tres));

			a.Ficha.hiddenpotential = 1000;   // (2026-10-26) o banco do potencial, que o fim solta 2x
			double bpAntes = a.Ficha.BP;
			efeitos.Clear();
			long agora0 = NowMs();
			Andar(RitualDoKaioshin.DuracaoMs);
			List<string> fim = Tique();
			AfirmarRk("E6. **NO FIM O BANCO DO POTENCIAL SAI 2x** (o dono, 2026-10-25: mil guardados, +2.000 de BP base, o banco zera e o ritual fica gasto pra este personagem) **E A FORMA ABRE**",
					  a.Ficha.unlockPotential >= 1 && Math.Abs(a.Ficha.BP - (bpAntes + 2000)) < 1e-6 && a.Ficha.hiddenpotential == 0
					  && a.Ficha.ritualDoKaioshinRecebido && TemAForma(a) && Disse(fim, "o Potential Unleashed é seu"),
					  $"potencial {a.Ficha.unlockPotential}, BP {bpAntes:0} -> {a.Ficha.BP:0}, banco {a.Ficha.hiddenpotential:0}, forma {TemAForma(a)} | {Ultimos(fim)}");
			AfirmarRk("E7. ...os dois se soltam (andam de novo, o efeito apaga, o fato apaga), e o Kaioshin ouve quando pode fazer outro",
					  !EmRitual(k) && !EmRitual(a) && PodeMexerOCorpo(k) && PodeMexerOCorpo(a)
					  && efeitos.Any(e => e.Quem == a.Id && e.Id == RitualDoKaioshin.EfeitoDaTrava && e.Ms == 0)
					  && (FatosDe(a) & Fatos.RitualDoKaioshin) == 0 && Disse(fim, "O seu próximo ritual em 24h00m"),
					  Ultimos(fim));
			AfirmarRk("E8. ...e o dia do Kaioshin se gasta NO FIM (a ficha guarda a hora)",
					  k.Ficha.RitualDoKaioshinEm >= agora0 + RitualDoKaioshin.DuracaoMs, $"{k.Ficha.RitualDoKaioshinEm - agora0}");
			EscutaDeEfeitos = null;

			for (int i = 0; i < 3; i++) ReconciliarDadiva(a);
			AfirmarRk("E9. A FORMA DO RITUAL FICA: tres reconciliacoes do cargo depois, ela continua (nao e skill de livro, e direito gravado)",
					  TemAForma(a) && !a.Livro.Sabe(RitualDoKaioshin.SkillDoMistico));

			// O C LEVA A FORMA CONCEDIDA -- e quem recebeu e um Kai sem escada de raca nenhuma.
			a.Ficha.Ki = a.Ficha.MaxKi;
			ConcedidaSemEscadaDeTeste = true;
			List<string> semEscada;
			try { semEscada = Ouvir(() => Transformar(a, subir: true)); }
			finally { ConcedidaSemEscadaDeTeste = false; }
			AfirmarRk("E10x. (defeito injetado: a forma concedida nao conta como escada) a regua do E10 REPROVA -- o Kai ouve \"sua raca nao tem essa escada\"",
					  a.Forma.Atual != Catalogo.IdMistico && Disse(semEscada, "escada"), Ultimos(semEscada));
			a.Ficha.Ki = a.Ficha.MaxKi;
			List<string> virou = Ouvir(() => Transformar(a, subir: true));
			AfirmarRk("E10. **A TECLA C LEVA AO POTENTIAL UNLEASHED** -- mesmo o Kai sem escada de raca nenhuma, que antes ouvia que nao tinha escada",
					  a.Forma.Atual == Catalogo.IdMistico, $"{a.Forma.Atual} | {Ultimos(virou)}");
			a.Forma.Entrar(Catalogo.IdBase);
			AplicarForma(a);

			// O CONTRA-EXEMPLO DO FIM: o ritual inteiro de novo, com o fim que nao da nada.
			(ServerPlayer k2, ServerPlayer a2) = Par();
			Comecar(k2, a2);
			RitualSemDomDeTeste = true;
			try
			{
				Andar(RitualDoKaioshin.DuracaoMs + 1_000);
				Tique();
			}
			finally { RitualSemDomDeTeste = false; }
			AfirmarRk("E6x. (defeito injetado: o fim do ritual nao da nada) a regua do E6 REPROVA -- dez minutos parado, e nem o potencial nem a forma",
					  !EmRitual(a2) && a2.Ficha.unlockPotential < 1 && !TemAForma(a2));
		}

		// ============================================================ F. O DIA
		{
			(ServerPlayer k, ServerPlayer a) = Par();
			Comecar(k, a);
			Andar(RitualDoKaioshin.DuracaoMs + 1_000);
			Tique();
			// O ALVO DE AGORA ja tem tudo o que o ritual da: o segundo convite vai pra um alvo NOVO, no lugar dele.
			RemoverNpc(a);
			ServerPlayer outroAlvo = Gente("OutroAlvo", "Human", k.Pos + new Vec2(T, 0), Facing.West);
			k.AlvoId = outroAlvo.Id;

			// (meia hora e meio minuto de parede depois do fim: o que falta cai no meio de um minuto, 23h29m30s, e o "23h29m" nao depende
			// de quantos milissegundos a bancada leva entre o fim e o aperto)
			Andar(30 * 60_000 + 30_000);
			List<string> deNovo = Apertar(k);
			AfirmarRk("F1. **UM RITUAL POR DIA**: o Kaioshin que terminou um ha meia hora nao oferece outro -- e ouve quanto falta, em horas e minutos",
					  !Oferecido(outroAlvo) && Disse(deNovo, "já fez o Ritual Místico hoje") && Disse(deNovo, "23h29m"), Ultimos(deNovo));
			RitualSemDiaDeTeste = true;
			try
			{
				Apertar(k);
				AfirmarRk("F1x. (defeito injetado: o dia nao e conferido) a regua do F1 REPROVA -- o segundo convite do dia sai", Oferecido(outroAlvo));
			}
			finally { RitualSemDiaDeTeste = false; Responder(outroAlvo, sim: false); }

			Andar(RitualDoKaioshin.RecargaMs);
			Apertar(k);
			AfirmarRk("F2. ...e um dia REAL depois (24 h de relogio de parede) ele oferece de novo", Oferecido(outroAlvo));
			Responder(outroAlvo, sim: false);

			// O INTERROMPIDO NAO GASTA O DIA.
			(ServerPlayer k3, ServerPlayer a3) = Par();
			Comecar(k3, a3);
			Andar(5 * RitualDoKaioshin.AvisoACadaMs);
			Tique();
			List<string> parou = Apertar(k3);
			List<string> denovo3 = Apertar(k3);
			AfirmarRk("F3. **O RITUAL INTERROMPIDO NAO GASTA O DIA**: o Kaioshin para no quinto minuto (o botao) e oferece de novo na hora",
					  Disse(parou, "foi interrompido") && Disse(parou, "não foi gasto") && Oferecido(a3) && k3.Ficha.RitualDoKaioshinEm == 0,
					  $"{Ultimos(parou)} || {Ultimos(denovo3)}");
			Responder(a3, sim: false);

			(ServerPlayer k4, ServerPlayer a4) = Par();
			DiaDoRitualNoComecoDeTeste = true;
			try
			{
				Comecar(k4, a4);
				Apertar(k4);
				Apertar(k4);
			}
			finally { DiaDoRitualNoComecoDeTeste = false; }
			AfirmarRk("F3x. (defeito injetado: o dia se gasta no comeco) a regua do F3 REPROVA -- interrompido, o Kaioshin nao oferece de novo",
					  !Oferecido(a4) && k4.Ficha.RitualDoKaioshinEm > 0);
		}

		// ============================================================ G. O QUE QUEBRA O RITUAL
		{
			// Cada causa num par novo, com o ritual de pe; a regua e a mesma: quebrou com o motivo, os dois soltos, nada despertado.
			string Quebra(string codigo, string oque, string motivo, Action<ServerPlayer, ServerPlayer> causa, bool tiquar = true)
			{
				(ServerPlayer k, ServerPlayer a) = Par();
				Comecar(k, a);
				bool comecou = EmRitual(k) && EmRitual(a);
				List<string> falas = Ouvir(() => { causa(k, a); if (tiquar) TickDosRituais(); });
				bool ok = comecou && !EmRitual(k) && !EmRitual(a) && Disse(falas, "foi interrompido") && Disse(falas, motivo)
						  && a.Ficha.unlockPotential < 1 && !TemAForma(a) && k.Ficha.RitualDoKaioshinEm == 0;
				AfirmarRk($"{codigo} {oque}", ok, Ultimos(falas));
				return Ultimos(falas);
			}

			Quebra("G1.", "QUEM RECEBE O RITUAL O INTERROMPE pelo mesmo botao (ele o ve enquanto dura) -- os dois se soltam, e o dia nao se gasta",
				   "Alvo interrompeu", (_, a) => UsarHabilidade(a, RitualDoKaioshin.Verbo), tiquar: false);
			Quebra("G2.", "**SAIR DO LUGAR QUEBRA O RITUAL**: o alvo arrastado um tile pra longe (um arremesso, um teleporte) solta os dois",
				   "Alvo saiu do lugar", (_, a) => a.Pos += new Vec2(T, 0));
			Quebra("G3.", "UM GOLPE TROCADO quebra o ritual (a briga com alguem acende)", "Alvo entrou numa luta",
				   (_, a) => a.Combate.EntrarEmCombate());
			Quebra("G4.", "A QUEDA quebra o ritual (o alvo nocauteado)", "Alvo caiu", (_, a) => a.Combate.Nocautear(30));
			Quebra("G5.", "O KAIOSHIN QUE PERDE O TRONO no meio do ritual o quebra", "não é mais o Kaioshin",
				   (_, _) => _tronos.Remove(RitualDoKaioshin.Cargo));
			Quebra("G6.", "QUEM SAI DO MUNDO quebra o ritual (o Kaioshin deslogou)", "Kaioshin saiu do mundo", (k, _) => RemoverNpc(k));
			Quebra("G7.", "QUEM VAI PRA OUTRA ZONA quebra o ritual", "Alvo foi pra outro lugar",
				   (_, a) => MoveToZone(a.Id, ZoneKey.Premade(Alem.ZonaDoOutroMundo), a.Pos));

			(ServerPlayer kx, ServerPlayer ax) = Par();
			Comecar(kx, ax);
			RitualNaoQuebraDeTeste = true;
			try
			{
				ax.Pos += new Vec2(T, 0);
				TickDosRituais();
				AfirmarRk("G2x. (defeito injetado: nada quebra o ritual) a regua do G2 REPROVA -- o alvo arrastado pra longe e o ritual continua", EmRitual(ax));
			}
			finally { RitualNaoQuebraDeTeste = false; }
		}

		// ============================================================ H. QUEM JA DESPERTOU, E KAIOSHIN EM KAIOSHIN
		{
			(ServerPlayer k, ServerPlayer a) = Par();
			a.Ficha.unlockPotential = 1;
			a.Ficha.hiddenpotential = 500;
			double bp = a.Ficha.BP;
			Comecar(k, a);
			Andar(RitualDoKaioshin.DuracaoMs + 1_000);
			Tique();
			AfirmarRk("H1. QUEM JA DESPERTOU O POTENCIAL POR OUTRO CAMINHO ainda tira do ritual o banco 2x e a forma (2026-10-26; era so a forma): +1.000",
					  TemAForma(a) && Math.Abs(a.Ficha.BP - (bp + 1000)) < 1e-6, $"BP {bp:0} -> {a.Ficha.BP:0}, forma {TemAForma(a)}");

			(ServerPlayer kA, ServerPlayer kB) = Par(racaDoAlvo: "Kai");
			_tronos[RitualDoKaioshin.Cargo + "#2"] = kB.Conta;   // os dois sao Kaioshin (cinco vagas)
			ReconciliarDadiva(kB);
			bool ehKaioshin = EhKaioshinNoTrono(kB) && !TemAForma(kB);
			Comecar(kA, kB);
			Andar(RitualDoKaioshin.DuracaoMs + 1_000);
			Tique();
			AfirmarRk("H2. **O KAIOSHIN PRECISA QUE OUTRO KAIOSHIN FACA O RITUAL NELE** (o dono): o segundo Kaioshin recebe o Potential Unleashed do primeiro",
					  ehKaioshin && TemAForma(kB) && EhKaioshinNoTrono(kB));
		}

	}
}
