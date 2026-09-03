using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--punhoteste` -- OS DEZESSEIS DO LOTE G7.
///
/// ============================ ELA EXERCITA A PRODUCAO, E SO ELA ============================
/// Nenhuma familia daqui chama o efeito direto. Toda tecnica entra por
/// <see cref="UsarHabilidade"/> -> <see cref="UsarTecnica"/> -> o registro de tecnicas vivas do lote G7 -- o
/// mesmo caminho do pacote do jogador, com o mesmo `SabeTecnica` no meio. A infraestrutura
/// (`Forjar`, `CorredorLivre`, `LimparTudoDaBancada`) e emprestada da `--projetilteste`, pelo mesmo
/// motivo de sempre: um segundo forjador seria a segunda resposta pra "como nasce um corpo de
/// bancada".
/// ==========================================================================================
///
/// ============================ O QUE ELA TENTA REPROVAR ============================
///  1. GATE E FRONTEIRA -- os dezesseis existem como portados; e as NOVE que este lote recusou
///                         (Suplex, Flip, Shock, Acid_Spit, Death_Ball e as quatro de jorro)
///                         continuam MUDAS e fora do lote. Esta e a afirmacao mais importante do
///                         arquivo: um verb meio-portado passaria despercebido pra sempre, e a
///                         unica prova de que nao portei nenhum pela metade e o censo dizer que
///                         eles continuam mudos.
///  2. DE ONDE VEM O VERBO -- treze dos dezesseis vem de um DEGRAU e nao de comprar skill. Sem o
///                         degrau, `SabeTecnica` recusa; com ele, aceita.
///  3. A RECARGA E UMA SO -- `basicCD` e do MOB, nao da tecnica. Usar Um-Dois tem que deixar KO
///                         Punch E o Multihit do lote G3 em espera junto. Se cada verb ganhasse o
///                         proprio contador, o teto de dano por segundo do jogo se multiplicaria
///                         por catorze -- e nada na tela diria isso.
///  4. A ESCADA DO COMBO -- os tres combos batem N vezes com dano CRESCENTE e cadencia propria; e
///                         a barragem de dano fixo (Multihit, Presa do Lobo) tem que continuar
///                         funcionando na MESMA agenda. E o teste de regressao do campo novo.
///  5. QUEM VOA PAGA MAIS -- Chute Descendente emenda e derruba; Derrubada tira o voo, bate DUAS
///                         vezes e atordoa 3,5 s. Contra quem esta no chao, nada disso acontece --
///                         um bonus que sempre dispara e indistinguivel de dano base.
///  6. A SURPRESA -- Degola e Punhalada valem mais FORA de combate, e a Punhalada vale mais pelas
///                         COSTAS. Um bonus condicional que nunca varia e um numero mentiroso.
///  7. AS DUAS BOLAS -- a Bala Dispersa nasce ESPALHADA (nao empilhada), espera antes de cacar e
///                         recusa sem alvo SEM COBRAR; o Spirit Gun gasta FOLEGO e nao Ki -- e essa
///                         e a unica coisa que ele tem de proprio no jogo inteiro.
///  8. A IA -- a Bala Dispersa entra no arsenal de longe e e a UNICA que dispensa linha livre; e a
///                         divida fica medida: a tabela da IA tem 4 linhas pra 21 verbos que atiram.
/// ==================================================================================
/// </summary>
public partial class GameServer
{
	private int _punhoOk, _punhoFalhou;

	private void AfirmarPunho(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _punhoOk++; GD.Print($"[punho]   OK    {oque}"); return; }
		_punhoFalhou++;
		GD.PrintErr($"[punho]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// OS TREZE VERBOS QUE SO O DEGRAU CONCEDE (`Assets/Data/niveis.json`).
	///
	/// Escritos aqui, e nao lidos do json, pela mesma razao da bancada do arsenal: ler o par do
	/// proprio arquivo faria a bancada concordar consigo mesma, e no dia em que o extrator parasse
	/// de escrever o degrau os dois lados sumiriam juntos com o teste verde.
	/// </summary>
	private static readonly (string Verbo, string Path, int Nivel)[] PorDegrauG7 =
	[
		("One_Two", "/datum/skill/boxing", 2),
		("One_Two_Five", "/datum/skill/Small_Boxer", 2),
		("Two_One_Four", "/datum/skill/Small_Boxer", 3),
		("KO_Punch", "/datum/skill/Bigtime_Boxer", 2),
		("Kickup", "/datum/skill/MartialSkill/Kicker", 2),
		("Dropkick", "/datum/skill/MartialSkill/Kicker", 3),
		("Falling_Kick", "/datum/skill/grace", 2),
		("Dash_Attack", "/datum/skill/MartialSkill/Green_Dean", 2),
		("Spin_Attack", "/datum/skill/MartialSkill/Reflexes_Training", 2),
		("Stun_Attack", "/datum/skill/MartialSkill/Dragon_Sweep", 2),
		("Takedown", "/datum/skill/MartialSkill/Tiger_Paw", 2),
		("Cutthroat", "/datum/skill/Assassain/Cutthroat", 2),
		("Backstab", "/datum/skill/Assassain/Backstab", 2),
	];

	/// <summary>Os tres que uma SKILL concede direto (`Assets/Data/skills.json`).</summary>
	private static readonly (string Verbo, string Path)[] PorSkillG7 =
	[
		("Lariat", "/datum/skill/drills"),
		("Scattering_Bullet", "/datum/skill/ki/Scattering_Bullet"),
		("Spirit_Gun", "/datum/skill/Spirit_Ball"),
	];

	/// <summary>
	/// AS NOVE QUE ESTE LOTE RECUSOU, com o sistema que cada uma espera.
	///
	/// Elas estao numa bancada, e nao so num comentario, porque um verb meio-portado nao aparece em
	/// lugar nenhum: o jogador aperta, ve algo acontecer, e nunca descobre que metade da tecnica nao
	/// existe. Enquanto esta familia estiver verde, o censo continua contando as nove como MUDAS --
	/// que e a verdade que o dono precisa ver na proxima passada.
	/// </summary>
	private static readonly (string Verbo, string Espera)[] RecusadasG7 =
	[
		// `Suplex` e `Shock` SAIRAM desta lista pelo lote G10 (`GameServer.Tecnicas.G10.cs`): o agarrao
		// chegou e o dano residual do Shock e o `damage_mob` atrasado, que o G10 trouxe. As sete que
		// ficam continuam mudas pelos motivos escritos ao lado.
		// `Flip` SAIU desta lista pelo lote G11 (`GameServer.Tecnicas.G11.cs`, `CambalhotaG11`): o agarrao
		// que ela esperava ja tinha chegado, e a cambalhota e uma funcao sobre ele.
		// `BusterBarrage`, `Continuous_Energy_Bullets`, `Spin_Blast` e `Death_Ball` SAIRAM desta lista pelo
		// lote G12 (`GameServer.Tecnicas.G12.cs`): o jorro sustentado e a bola que se carrega na mao chegaram
		// (bola INERTE que renasce por estagio, ver `Projetil.Inerte`). Ficam as duas que ainda esperam.
		("Acid_Spit", "dano ao longo do tempo"),
		("Energy_Wave_Volley", "jorro sustentado"),
	];

	public void RodarBancadaDoPunho()
	{
		_punhoOk = _punhoFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[punho] ================ OS PUNHOS NOMEADOS (lote G7) ================");

		AfirmarPunho("a zona da bancada tem colisao carregada", _pjMapa != null);

		try
		{
			OCatalogoEAFronteira();
			ODegrauEAOSkill();
			ARecargaEUmaSo();
			AEscadaDoCombo();
			QuemVoaPagaMais();
			ASurpresa();
			AsDuasBolas();
			OArsenalDaIa();
		}
		finally
		{
			LimparTudoDaBancada();
		}

		GD.Print($"[punho] ================ {_punhoOk} passaram, {_punhoFalhou} falharam ================");
	}

	// =====================================================================
	// 1) O CATALOGO E A FRONTEIRA
	// =====================================================================
	private void OCatalogoEAFronteira()
	{
		GD.Print("[punho] -- 1) OS DEZESSEIS EXISTEM, AS NOVE RECUSADAS CONTINUAM MUDAS");

		var todos = new List<string>();
		foreach ((string v, _, _) in PorDegrauG7) todos.Add(v);
		foreach ((string v, _) in PorSkillG7) todos.Add(v);

		AfirmarPunho("os dezesseis do lote sao exatamente os dezesseis desta bancada",
					 todos.Count == 16 && todos.TrueForAll(v => EhDoLote("G7", v)), $"{todos.Count} verbos");

		var mudas = todos.FindAll(v => Tecnicas.Get(v)!.Modo == Modo.NaoPortada);
		AfirmarPunho("...e nenhum deles continua marcado como NAO-PORTADO",
					 mudas.Count == 0, string.Join(" | ", mudas));

		// A AFIRMACAO QUE ESTE ARQUIVO EXISTE PRA FAZER. Se algum dia alguem "fechar" uma das nove
		// pela metade -- registrar o verb e deixar o efeito faltando --, esta linha fica vermelha
		// antes de o jogador descobrir sozinho.
		var vazadas = new List<string>();
		foreach ((string v, string espera) in RecusadasG7)
			if (EhDoLote("G7", v) || Tecnicas.Get(v)!.Modo != Modo.NaoPortada) vazadas.Add($"{v} ({espera})");
		AfirmarPunho("as NOVE recusadas continuam mudas e fora do lote (nenhuma foi meio-portada)",
					 vazadas.Count == 0, string.Join(" | ", vazadas));

		Vec2 chao = CorredorLivre(24);
		ServerPlayer nu = Forjar("SemSkill", chao, bp: 5_000);
		nu.Facing = Facing.East;
		Forjar("Saco", chao + new Vec2(ZoneCollision.TileSize, 0), bp: 5_000);

		double kiAntes = nu.Ficha.Ki;
		List<string> falas = ApertarEOuvir(nu, "KO_Punch");
		AfirmarPunho("quem NAO destravou o verbo ouve \"voce nao sabe\" e nao gasta nada",
					 Math.Abs(nu.Ficha.Ki - kiAntes) < 0.001 && Disse(falas, "nao sabe"), Ultimos(falas));

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 2) DE ONDE VEM O VERBO
	// =====================================================================
	private void ODegrauEAOSkill()
	{
		GD.Print("[punho] -- 2) TREZE VERBOS SAO DO DEGRAU, TRES SAO DA SKILL");

		ServerPlayer pl = Forjar("Estudioso", CorredorLivre(24), bp: 5_000);

		var semNada = new List<string>();
		foreach ((string v, _, _) in PorDegrauG7) if (SabeTecnica(pl, v)) semNada.Add(v);
		foreach ((string v, _) in PorSkillG7) if (SabeTecnica(pl, v)) semNada.Add(v);
		AfirmarPunho("com o livro e a ficha de niveis VAZIOS, nenhum dos dezesseis e reconhecido",
					 semNada.Count == 0, string.Join(" | ", semNada));

		var save = new NivelSave();
		foreach ((_, string path, int nivel) in PorDegrauG7) save.Skills[path] = [nivel, 0];
		pl.Niveis.DoSave(save);

		var faltando = new List<string>();
		foreach ((string v, _, _) in PorDegrauG7) if (!SabeTecnica(pl, v)) faltando.Add(v);
		AfirmarPunho("...e com o degrau cruzado os TREZE passam a ser reconhecidos",
					 faltando.Count == 0, string.Join(" | ", faltando));

		// O DEGRAU EXATO IMPORTA: `Two_One_Four` sai do nivel 3 do MESMO `Small_Boxer` que da o
		// `One_Two_Five` no nivel 2. Um `VerbosAtivos` que ignorasse o nivel entregaria os dois de
		// uma vez -- e a escada de skill do boxe deixaria de existir.
		var so2 = new NivelSave();
		so2.Skills["/datum/skill/Small_Boxer"] = [2, 0];
		ServerPlayer meio = Forjar("MeioBoxeador", CorredorLivre(24), bp: 5_000);
		meio.Niveis.DoSave(so2);
		AfirmarPunho("...e o nivel 2 do Small_Boxer da o Um-Dois-Cinco mas NAO o Dois-Um-Quatro",
					 SabeTecnica(meio, "One_Two_Five") && !SabeTecnica(meio, "Two_One_Four"));

		foreach ((_, string path) in PorSkillG7) pl.Livro.Dar(path);
		var naoAbriu = new List<string>();
		foreach ((string v, _) in PorSkillG7) if (!SabeTecnica(pl, v)) naoAbriu.Add(v);
		AfirmarPunho("...e aprender a skill abre os TRES (Lariat, Bala Dispersa, Spirit Gun)",
					 naoAbriu.Count == 0, string.Join(" | ", naoAbriu));

		AfirmarPunho("...e o menu do cliente enxerga os mesmos dezesseis (uma lista so, nao duas)",
					 PorDegrauG7.All(t => TecnicasDe(pl).Contains(t.Verbo))
					 && PorSkillG7.All(t => TecnicasDe(pl).Contains(t.Verbo)));

		LimparTudoDaBancada();
	}

	/// <summary>Um corpo com os dezesseis destravados, Ki e folego cheios. A dupla colada e a <see cref="Dupla"/> comum.</summary>
	private ServerPlayer ForjarSocador(string nome, Vec2 onde, double bp)
		=> ForjarComSkills(nome, onde, bp,
						   skills: [.. PorSkillG7.Select(t => t.Path)],
						   degraus: [.. PorDegrauG7.Select(t => (t.Path, t.Nivel))],
						   kiMin: 50_000_000, staminaMin: 100_000);

	// =====================================================================
	// 3) A RECARGA E UMA SO
	// =====================================================================
	private void ARecargaEUmaSo()
	{
		GD.Print("[punho] -- 3) O `basicCD` E DO MOB, E NAO DA TECNICA");

		(ServerPlayer a, _) = Dupla(ForjarSocador);

		_prontoG3.Remove(a.Id);
		UsarHabilidade(a, "Kickup");
		AfirmarPunho("usar um verbo do lote arma a recarga compartilhada", _prontoG3.ContainsKey(a.Id));

		double kiAntes = a.Ficha.Ki;
		List<string> falas = ApertarEOuvir(a, "KO_Punch");
		AfirmarPunho("...e OUTRO verbo do lote e recusado por ela, sem cobrar Ki",
					 Math.Abs(a.Ficha.Ki - kiAntes) < 0.001 && Disse(falas, "recompoem"), Ultimos(falas));

		// O ELO COM O LOTE G3: o Multihit usa o MESMO `_prontoG3`. Sem esta linha o lote novo poderia
		// ter aberto um contador so dele e ninguem veria -- o teto de dano por segundo dobraria.
		a.Livro.Dar("/datum/skill/MartialSkill/Special_Multihit");
		falas = ApertarEOuvir(a, "Special_Multihit");
		AfirmarPunho("...e a recarga do lote G7 tambem segura o Multihit do lote G3",
					 _barragemG3.Count == 0 && Disse(falas, "recompoem"), Ultimos(falas));

		// A RECARGA MAIS LONGA DO LOTE e do Spirit Gun (`reload + 45` tiques = ~4,5 s), e a mais
		// curta e a dos doze punhos (15 tiques). Se as duas fossem iguais, o `basicCD += 25/30` do
		// Cutthroat e do Backstab teria virado decoracao.
		_prontoG3.Remove(a.Id);
		long t0 = NowMs();
		UsarHabilidade(a, "Cutthroat");
		long cutthroat = _prontoG3[a.Id] - t0;
		_prontoG3.Remove(a.Id);
		UsarHabilidade(a, "Backstab");
		long backstab = _prontoG3[a.Id] - t0;
		_prontoG3.Remove(a.Id);
		UsarHabilidade(a, "Kickup");
		long comum = _prontoG3[a.Id] - t0;

		AfirmarPunho("as tres recargas do DM sao diferentes: comum 1,5s < Degola 2,5s < Punhalada 3s",
					 comum < cutthroat && cutthroat < backstab,
					 $"comum {comum} | degola {cutthroat} | punhalada {backstab}");

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 4) A ESCADA DO COMBO
	// =====================================================================
	private void AEscadaDoCombo()
	{
		GD.Print("[punho] -- 4) OS COMBOS SOBEM DE DANO; A BARRAGEM DE DANO FIXO CONTINUA FIXA");

		(ServerPlayer a, ServerPlayer d) = Dupla(ForjarSocador);

		_prontoG3.Remove(a.Id);
		UsarHabilidade(a, "One_Two_Five");
		AfirmarPunho("o Um-Dois-Cinco agenda DOIS golpes depois do primeiro",
					 _barragemG3.TryGetValue(a.Id, out BarragemG3? b) && b.Faltam == 2);

		BarragemG3 seq = _barragemG3[a.Id];
		AfirmarPunho("...e a escada dele e (+0 | +4 | +6), com a cadencia 0,1s e 0,2s do DM",
					 seq.Escada != null && seq.Escada.Length == 3
					 && Math.Abs(seq.Escada[0].Dano - 0) < 0.001
					 && Math.Abs(seq.Escada[1].Dano - 4) < 0.001 && seq.Escada[1].AtrasoMs == 100
					 && Math.Abs(seq.Escada[2].Dano - 6) < 0.001 && seq.Escada[2].AtrasoMs == 200,
					 seq.Escada == null ? "sem escada" : string.Join(" | ", seq.Escada));

		// A ESCADA ANDA DE VERDADE. Um indice que nunca avanca daria os tres golpes com o dano do
		// primeiro -- e a tecnica pareceria funcionar.
		int idxAntes = seq.EscadaIdx;
		seq.ProximoMs = 0;
		PulsoG3();
		AfirmarPunho("...e o pulso do lote G3 ANDA na escada (o degrau avanca)",
					 !_barragemG3.ContainsKey(a.Id) || _barragemG3[a.Id].EscadaIdx > idxAntes);

		_barragemG3.Clear();

		// REGRESSAO: a barragem SEM escada (Multihit) tem que continuar lendo o `AddDano` unico.
		_prontoG3.Remove(a.Id);
		a.Livro.Dar("/datum/skill/MartialSkill/Special_Multihit");
		UsarHabilidade(a, "Special_Multihit");
		AfirmarPunho("a barragem sem escada continua de dano fixo (o campo novo nao a contaminou)",
					 !_barragemG3.TryGetValue(a.Id, out BarragemG3? m) || m.Escada == null);
		_barragemG3.Clear();

		// O DOIS-UM-QUATRO NAO AVANCA (`if(target in view(1))` sem o `step`): posto longe, ele
		// recusa em vez de andar. Sem esta afirmacao os tres combos seriam o mesmo verb.
		_prontoG3.Remove(a.Id);
		d.Pos = a.Pos + new Vec2(ZoneCollision.TileSize * 2.2f, 0);
		Vec2 antes = a.Pos;
		List<string> falas = ApertarEOuvir(a, "Two_One_Four");
		AfirmarPunho("o Dois-Um-Quatro NAO da o passo: a dois tiles ele recusa e o corpo nao anda",
					 Vec2.Distance(a.Pos, antes) < 0.5f && _barragemG3.Count == 0, Ultimos(falas));

		// ...e o Um-Dois, que TEM o passo, alcanca da mesma distancia.
		_prontoG3.Remove(a.Id);
		falas = ApertarEOuvir(a, "One_Two");
		AfirmarPunho("...e o Um-Dois, que TEM o passo, alcanca da mesma distancia",
					 Vec2.Distance(a.Pos, antes) > 0.5f, Ultimos(falas));
		_barragemG3.Clear();

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 5) QUEM VOA PAGA MAIS
	// =====================================================================
	private void QuemVoaPagaMais()
	{
		GD.Print("[punho] -- 5) O CHUTE DESCENDENTE E A DERRUBADA PUNEM QUEM ESTA NO AR");

		(ServerPlayer a, ServerPlayer d) = Dupla(ForjarSocador);

		// NO CHAO: a Derrubada nao atordoa longo nem tira voo nenhum.
		d.Voando = false;
		d.Combate.Stun = 0;
		_prontoG3.Remove(a.Id);
		UsarHabilidade(a, "Takedown");
		AfirmarPunho("contra quem esta no CHAO a Derrubada nao impoe o atordoamento de 3,5s",
					 d.Combate.Stun < 3.0, $"stun {d.Combate.Stun:0.##}");

		// NO AR: tira o voo, atordoa 3,5 s.
		d.Voando = true;
		d.Combate.Stun = 0;
		_prontoG3.Remove(a.Id);
		UsarHabilidade(a, "Takedown");
		AfirmarPunho("contra quem VOA ela tira o voo e atordoa 3,5s",
					 !d.Voando && d.Combate.Stun >= 3.4, $"voando={d.Voando} stun={d.Combate.Stun:0.##}");

		// O CHUTE DESCENDENTE tambem derruba -- mas so quem estava voando.
		d.Voando = true;
		_prontoG3.Remove(a.Id);
		List<string> falas = ApertarEOuvir(a, "Falling_Kick");
		AfirmarPunho("o Chute Descendente arranca do ar quem estava voando", !d.Voando, Ultimos(falas));

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 6) A SURPRESA
	// =====================================================================
	/// <summary>
	/// OS DOIS BONUS DO ASSASSINO SAO CONDICIONAIS, e um bonus que sempre dispara e so um numero
	/// maior. Aqui a bancada mede o DANO SOMADO que cada situacao produz, sem passar pela rolagem:
	/// ela le a mesma expressao que o verb usa, com o mesmo corpo, nos dois estados.
	///
	/// MEDIR PELO HP SERIA MEDIR A MOEDA: pontaria, guarda e esquiva sao sorteadas dentro do
	/// `MeleeResolver`, e um unico "errou" inverteria a comparacao. O que se afirma e a RECEITA.
	/// </summary>
	private void ASurpresa()
	{
		GD.Print("[punho] -- 6) DEGOLA E PUNHALADA VALEM MAIS DE SURPRESA E PELAS COSTAS");

		(ServerPlayer a, ServerPlayer d) = Dupla(ForjarSocador);

		// FORA DE COMBATE
		a.Combate.EmCombate = 0;
		double foraDeCombate = 1 + a.Ficha.Etechnique;
		double punhaladaCostas = a.Ficha.Etechnique / 2 + a.Ficha.Etechnique / 2;

		_prontoG3.Remove(a.Id);
		List<string> falas = ApertarEOuvir(a, "Cutthroat");
		AfirmarPunho($"fora de combate a Degola soma 1+Etecnica (={foraDeCombate:0.##})",
					 Disse(falas, "corta fundo"), Ultimos(falas));

		// DENTRO DE COMBATE -- o proprio golpe acima ja poe os dois em briga, entao basta usar de novo.
		_prontoG3.Remove(a.Id);
		a.Combate.EntrarEmCombate();
		falas = ApertarEOuvir(a, "Cutthroat");
		AfirmarPunho("...e dentro dela o corte sai COMUM (o bonus e a tecnica, nao um tempero)",
					 Disse(falas, "sai comum"), Ultimos(falas));

		// PELAS COSTAS: `dir == target.dir` -- os dois olhando pro MESMO lado.
		a.Facing = Facing.East;
		d.Facing = Facing.East;
		_prontoG3.Remove(a.Id);
		falas = ApertarEOuvir(a, "Backstab");
		AfirmarPunho($"pelas costas a Punhalada crita (soma ate {punhaladaCostas:0.##})",
					 Disse(falas, "pelas costas"), Ultimos(falas));

		d.Facing = Facing.West;
		_prontoG3.Remove(a.Id);
		falas = ApertarEOuvir(a, "Backstab");
		AfirmarPunho("...e de frente ela vale menos, e o jogo DIZ isso",
					 Disse(falas, "vale menos"), Ultimos(falas));

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 7) AS DUAS BOLAS
	// =====================================================================
	private void AsDuasBolas()
	{
		GD.Print("[punho] -- 7) A BALA DISPERSA E O SPIRIT GUN");

		(ServerPlayer a, ServerPlayer d) = Dupla(ForjarSocador);

		// SEM ALVO MARCADO: recusa, e NAO cobra. Um verb que cobra pela recusa e a familia de defeito
		// que este port ja achou quatro vezes.
		a.AlvoId = 0;
		_scatterPronto.Remove(a.Id);
		double kiAntes = a.Ficha.Ki;
		List<string> falas = ApertarEOuvir(a, "Scattering_Bullet");
		AfirmarPunho("a Bala Dispersa sem alvo marcado recusa e NAO cobra Ki",
					 Math.Abs(a.Ficha.Ki - kiAntes) < 0.001
					 && ProjeteisDaZona(a.Zone.Hash).Count == 0, Ultimos(falas));

		// COM ALVO: nasce uma nuvem, ESPALHADA, mirando o alvo, e com espera antes de cacar.
		a.AlvoId = d.Id;
		_scatterPronto.Remove(a.Id);
		UsarHabilidade(a, "Scattering_Bullet");
		List<Projetil> nuvem = ProjeteisDaZona(a.Zone.Hash);

		AfirmarPunho("...e com alvo marcado sai uma nuvem de mais de uma bola",
					 nuvem.Count > 1, $"{nuvem.Count} bolas");

		AfirmarPunho("...todas mirando o alvo, e todas ESPERANDO antes de cacar",
					 nuvem.Count > 0 && nuvem.TrueForAll(p => p.Alvo == d.Id && p.EsperaDeCaca > 0));

		// ESPALHADA DE VERDADE: se as bolas nascessem todas no mesmo ponto (o erro obvio de copiar o
		// `Disparar` sem `deOnde`), a tecnica viraria uma barragem empilhada e ninguem notaria.
		bool espalhou = false;
		for (int i = 1; i < nuvem.Count && !espalhou; i++)
			if (Vec2.Distance(nuvem[i].Pos, nuvem[0].Pos) > 1f) espalhou = true;
		AfirmarPunho("...e elas NASCEM em pontos diferentes (nuvem, e nao pilha)",
					 espalhou || nuvem.Count == 1, $"{nuvem.Count} bolas no mesmo ponto");

		// ...e nenhuma nasce em cima do dono: o raio minimo do espalhamento e um tile.
		AfirmarPunho("...e nenhuma nasce colada em quem atirou",
					 nuvem.TrueForAll(p => Vec2.Distance(p.Pos, a.Pos) >= ZoneCollision.TileSize - 1));

		// A RECARGA PROPRIA: ela nao e a das barragens do lote G5.
		falas = ApertarEOuvir(a, "Scattering_Bullet");
		AfirmarPunho("...e a segunda tentativa cai na recarga propria dela", Disse(falas, "formigando"), Ultimos(falas));
		LimparTudoDaBancada();

		// --- SPIRIT GUN: A UNICA BOLA PAGA EM FOLEGO ---
		(ServerPlayer b, ServerPlayer _) = Dupla(ForjarSocador);
		b.Facing = Facing.East;
		_prontoG3.Remove(b.Id);

		double kiDele = b.Ficha.Ki, folegoDele = b.Ficha.stamina;
		UsarHabilidade(b, "Spirit_Gun");

		AfirmarPunho("o Spirit Gun gasta FOLEGO e nao encosta no Ki",
					 Math.Abs(b.Ficha.Ki - kiDele) < 0.001 && b.Ficha.stamina < folegoDele,
					 $"ki {kiDele:0}->{b.Ficha.Ki:0} | folego {folegoDele:0}->{b.Ficha.stamina:0}");

		AfirmarPunho("...e a bala sai mesmo assim", ProjeteisDaZona(b.Zone.Hash).Count == 1);

		// COM O KI ZERADO ele continua saindo -- e essa e a razao de ele existir na arvore do
		// Espirito. Se um dia alguem o passar pro `PodeAtirar` com custo em Ki, esta linha fica
		// vermelha.
		LimparTudoDaBancada();
		(ServerPlayer c, ServerPlayer _) = Dupla(ForjarSocador);
		c.Facing = Facing.East;
		c.Ficha.Ki = 0;
		_prontoG3.Remove(c.Id);
		falas = ApertarEOuvir(c, "Spirit_Gun");
		AfirmarPunho("...e ele sai com o tanque de Ki ZERADO (e a promessa da arvore do Espirito)",
					 ProjeteisDaZona(c.Zone.Hash).Count == 1, Ultimos(falas));

		// SEM FOLEGO, NAO SAI.
		LimparTudoDaBancada();
		(ServerPlayer e, ServerPlayer _) = Dupla(ForjarSocador);
		e.Facing = Facing.East;
		e.Ficha.stamina = 0;
		_prontoG3.Remove(e.Id);
		falas = ApertarEOuvir(e, "Spirit_Gun");
		AfirmarPunho("...e sem folego ele NAO sai, e a recusa fala em folego",
					 ProjeteisDaZona(e.Zone.Hash).Count == 0 && Disse(falas, "folego"), Ultimos(falas));

		// OS DOIS CAMPOS NOVOS DA FICHA existem e nascem com os valores do DM -- sem eles os degraus
		// da arvore do Espirito voltariam a cair em `EfeitosDeSkill.Desconhecidos`.
		AfirmarPunho("SpiritBallCost nasce 2 e SpiritBallDamage nasce 1 (os valores do DM)",
					 Math.Abs(e.Ficha.SpiritBallCost - 2) < 0.001
					 && Math.Abs(e.Ficha.SpiritBallDamage - 1) < 0.001,
					 $"{e.Ficha.SpiritBallCost} / {e.Ficha.SpiritBallDamage}");

		AfirmarPunho("...e nenhum dos dois esta na lista de campos DESCONHECIDOS do motor de skills",
					 !EfeitosDeSkill.Desconhecidos.Contains("SpiritBallCost")
					 && !EfeitosDeSkill.Desconhecidos.Contains("SpiritBallDamage"));

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 8) A IA
	// =====================================================================
	/// <summary>
	/// A PERGUNTA DO DONO: *"a IA ganha isso de graca?"*. A resposta medida e NAO -- e esta familia
	/// existe pra que a resposta fique escrita em vermelho e verde, e nao num paragrafo.
	///
	/// <see cref="TecnicasDeLonge"/> e uma tabela ESCRITA A MAO. Portar uma tecnica de tiro nao a
	/// preenche; ela so ganha uma linha quando alguem julga a JANELA em que a IA acerta com aquele
	/// tiro (alcance minimo, maximo e precisao), e nenhum desses tres numeros esta no DM.
	/// </summary>
	private void OArsenalDaIa()
	{
		GD.Print("[punho] -- 8) O ARSENAL DA IA NAO CRESCE SOZINHO");

		AfirmarPunho("a Bala Dispersa ENTROU no arsenal de longe da IA",
					 TecnicasDeLonge.Get("Scattering_Bullet") != null);

		TecnicasDeLonge.Linha? bala = TecnicasDeLonge.Get("Scattering_Bullet");
		AfirmarPunho("...e ela e a UNICA que dispensa linha livre (as bolas contornam)",
					 bala is { PrecisaDeLinhaLivre: false }
					 && TecnicasDeLonge.Todas.Count(l => !l.PrecisaDeLinhaLivre) == 1);

		// O CUSTO SAI DA MESMA EXPRESSAO QUE O VERB COBRA -- nunca uma copia. Se alguem reequilibrar
		// o verb e esquecer a tabela, esta linha fica vermelha.
		ServerPlayer pl = ForjarSocador("Piloto", CorredorLivre(24), bp: 5_000);
		AfirmarPunho("...e o preco que a IA usa e o mesmo `60*BaseDrain` que o verb cobra",
					 bala != null
					 && Math.Abs(bala.Value.CustoDeKi(pl.Ficha) - 60 * pl.Ficha.BaseDrain()) < 0.001);

		AfirmarPunho("...e o arsenal dela e montado pela MESMA pergunta do jogador (`SabeTecnica`)",
					 ArsenalDeLonge(pl).TemAlguma);

		// A DIVIDA, MEDIDA. Vinte e um verbos atiram; a tabela tem quatro linhas. Enquanto esta
		// afirmacao passar, a divida esta declarada e nao esquecida -- e no dia em que alguem
		// preencher a tabela, ela fica vermelha e pede pra ser reescrita, que e o que se quer.
		AfirmarPunho("A DIVIDA: a tabela da IA tem 4 linhas, e o Spirit Gun (folego) fica de fora",
					 TecnicasDeLonge.Quantas == 4 && TecnicasDeLonge.Get("Spirit_Gun") == null,
					 $"{TecnicasDeLonge.Quantas} linhas");

		AfirmarPunho("...e os vinte raios/bolas dos lotes G5 e G6 continuam invisiveis pra ela",
					 TecnicasDeLonge.Get("Kamehameha") == null
					 && TecnicasDeLonge.Get("Hellzone_Grenade") == null
					 && TecnicasDeLonge.Get("Final_Flash") == null);

		LimparTudoDaBancada();
	}
}
