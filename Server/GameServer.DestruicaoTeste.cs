using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA DESTRUICAO DE PLANETA (`--planetateste`).
///
/// ============================ O QUE SO DAQUI SE RESPONDE ============================
///   1. **A SEQUENCIA TEM OS NUMEROS DO DM?** Os quatro estagios de 200/300/300/400 s, a explosao de
///      310 s, a carga de 30 s. O relogio e adiantado pela MESMA funcao de producao
///      (`TickDaDestruicao`), um segundo por volta -- nao ha atalho que pule direto pro commit.
///   2. **O PAVIO RETOMA, E NAO REINICIA, DEPOIS DE UM REINICIO?** Esta e A checagem deste arquivo.
///      O defeito do original (`Weather.dm:72-74`) e que `planet_dying` persiste e
///      `planet_death_stage` nao, entao **todo boot recomeca a morte do estagio 0**. A bancada grava,
///      apaga o registro em memoria, le do disco e confere que o estagio e o resto do relogio
///      voltaram IGUAIS.
///   3. **SERVIDOR VAZIO ADIA O PAVIO -- e NAO adia a explosao?** As duas metades. So a primeira
///      seria um sistema que nunca acontece; so a segunda seria o planeta explodindo de madrugada.
///   4. **O COMMIT SEPARA QUEM E MAIS FORTE?** Quem esta muito acima do limiar DESTE CHAO atravessa o
///      fim do mundo -- **machucado, nunca ileso**; quem esta abaixo leva a furia inteira em cada
///      membro; quem esta NOCAUTEADO morre. (A regua deixou de ser o BP do algoz -- ver
///      `MortePlanetaria.DanoNoCorpo`.)
///  12. **A FORMULA E UMA SO, E ELA LE O MUNDO?** Tamanho x gravidade, com as entradas vindo do
///      manifesto e do `planetas.json` (e nao de um padrao calado), respondendo tambem pra mundo
///      gerado que ninguem carregou, e com as duas populacoes na mesma regua.
///  13. **A RAMPA E CONSUMIDA, e nao so escrita?** Ceu, tremor, chao e cratera medidos ANTES e DEPOIS
///      no tique de producao. Esta e a armadilha nomeada deste projeto: uma rampa perfeita que
///      nenhum efeito lesse ficaria 100% verde.
///  14. **AS DUAS SAIDAS PRO CADAVER FORAM FECHADAS?** Quem sai do Templo (a passagem cravada no
///      mapa) e quem volta de um logout. As duas metades: com o planeta vivo os dois continuam
///      funcionando.
///   5. **A MORTE PASSA PELA PORTA UNICA?** Um `NegarMorte` pendurado no corpo tem que barrar a morte
///      do commit -- se nao barrar, esta e a unica morte do jogo que ignora a Aura of Destruction.
///   6. **A EVACUACAO POE A PESSOA NO ESPACO, FORA DO RAIO?** E, portanto, sem re-pousar no quadro
///      seguinte -- e com o filtro de planeta morto impedindo a volta.
///   7. **OS CONSUMIDORES ESQUECIDOS ESTAO LIGADOS?** Povoamento, berco e pouso.
///   8. **O FIM DO MUNDO SEM PLATEIA CHEGA AO MESMO LUGAR?** Destruir um planeta VAZIO entra na lista
///      igual -- e a explosao **para na borda da zona**: uma testemunha fraca noutro planeta nao leva
///      um arranhao. Sem ela, um commit que varresse `_players` passaria despercebido.
///   9. **O CADAVER SOBREVIVE AO BOOT?** O bloco 2 mede o PAVIO voltando do disco; este mede o
///      estado FINAL voltando -- e o tique nao mexendo mais nele.
///  10. **O PLANETA GERADO RENASCE DESTRUIDO?** Ele nao existe entre dois boots: e refeito da seed
///      toda vez. E o irmao de MESMO NOME e outra seed continua vivo -- a linha que faz a
///      `ChaveDePlaneta` valer o que ela custou.
///  15. **DA PRA DERRUBAR UM MUNDO COM KI, DO ESPACO?** (K1-K6) Um tiro de verdade, no tique de
///      producao, nascendo FORA do disco. E o aviso varrido DIGITO POR DIGITO, porque o dono
///      proibiu numero. Ver <see cref="MedirODerrubarComKi"/>.
///  11. **OS GATES TEM OS DOIS SENTIDOS?** Vilao, os 10 marcos, o Ki, o BP contra a gravidade e os
///      30 s -- cada um visto recusando E deixando passar. Aqui o verbo entra por `UsarTecnica`, que
///      e a porta do cliente: e por ela que o custo de 10 marcos vira consequencia.
/// ================================================================================
///
///     Godot --headless -- --server --planetateste
/// </summary>
public partial class GameServer
{
	private bool _planetaDeTeste;

	/// <summary>
	/// Roda uma vez, no primeiro login. **MEXE NO MUNDO** (mata um planeta de verdade e escreve o
	/// `planetas-mortos.json`), entao tudo o que ela toca e fotografado e devolvido no `finally`.
	/// </summary>
	private void RodarBancadaDePlaneta(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DA DESTRUICAO DE PLANETA =====");

		int ok = 0, falhou = 0;
		int provasMataram = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		// Ela destroi um planeta PRE-FEITO de verdade. Sem esta fotografia, rodar a bancada uma vez
		// deixaria Arlia morta pra sempre no save do dono.
		// ==============================================================================
		var guardado = _mortos.Todos.ToList();

		// ============================ E O ARQUIVO DO DISCO TAMBEM VOLTA -- INCLUSIVE A AUSENCIA DELE ============================
		// A fotografia acima devolve o registro em MEMORIA; ela nao devolve o `planetas-mortos.json`.
		// E o bloco 2 desta bancada **precisa** gravar de verdade (ele mede o pavio voltando do disco),
		// entao rodar aqui deixava um arquivo onde nao havia nenhum -- e "nao existe" e justamente a
		// resposta que este sistema da pra "nenhum planeta esta morrendo" (`PlanetasMortos.cs:82`).
		//
		// Guardar os BYTES e nao so o "existia?": se o dono tiver um planeta agonizando de verdade, a
		// bancada grava por cima dele umas dez vezes e o estado final seria o da ULTIMA gravacao dela.
		// =====================================================================================================================
		string caminhoDoLivro = CaminhoDosMortos;
		bool livroExistia = System.IO.File.Exists(caminhoDoLivro);
		byte[]? livroDeAntes = livroExistia ? System.IO.File.ReadAllBytes(caminhoDoLivro) : null;

		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		double bpGuardado = pl.Ficha.BP, hpGuardado = pl.Ficha.HP, kiGuardado = pl.Ficha.Ki;
		bool viloGuardado = pl.Ficha.isVillain;
		bool mortoGuardado = pl.Ficha.dead, koGuardado = pl.Ficha.KO;

		// ============================ O LIVRO DE SKILLS E A GRAVIDADE TAMBEM VOLTAM ============================
		// A familia dos gates COMPRA a skill de vilao com marcos de mentira e mexe no `Planetgrav` pra
		// medir o limiar. Sem esta fotografia, quem rodasse a bancada uma vez ficaria com Planet
		// Destroy no livro e com o peso de um planeta que ele nao esta pisando -- e o `Persistir` la
		// embaixo gravaria as duas coisas no disco.
		// ================================================================================================
		var skillsGuardadas = pl.Livro.Aprendidas.ToList();
		int marcosGuardados = pl.Livro.MarcosLivres, totaisGuardados = pl.Livro.MarcosTotais;
		double gravGuardada = pl.Ficha.Planetgrav;

		// ============================ O CORPO PRECISA COMECAR VIVO -- E ISSO CUSTOU UMA RODADA ============================
		// A segunda rodada desta bancada caiu em DOZE checagens, e a causa nao estava em nenhuma
		// delas: o personagem voltou **morto** do save da rodada anterior (ela o matou no commit e o
		// `Persistir` gravou). Com ele morto, `AlguemOnline()` responde falso -- e o "servidor vazio
		// adia o pavio", que e regra de PRODUCAO e estava funcionando, congelou o pavio inteiro.
		//
		// Vale guardar por dois motivos. Primeiro: a bancada que MEXE no mundo tem que devolver
		// tambem o que ela quebrou no proprio operador, e nao so o mundo. Segundo, e mais util: o
		// sintoma ("o pavio nao anda") apontava pra um lugar onde nao havia defeito nenhum -- foi a
		// checagem de MONTAGEM logo abaixo que mostrou onde olhar.
		// ==========================================================================================================
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate?.Corpo.Curar(1e9);
		pl.Combate?.SincronizarVida();

		// ============================ E AS ESFERAS TAMBEM VOLTAM, DESDE QUE A MORTE AS ALCANCA ============================
		// Entrou junto com a regra *"set de esferas ancorado num mundo morto nao existe"*
		// (`EnterrarSetsDeMundosMortos`): a partir dela, **matar Namek apaga o Porunga** -- e restaura-lo
		// o ergue de novo, com CICLO novo, prazo novo e as sete em pontos novos.
		//
		// O `PalcoDeMortes` la embaixo protege o `planetas-mortos.json`, e nao o `esferas.json`: o
		// `SalvarEsferas` nao passa por ele. Sem esta fotografia, uma rodada desta bancada moveria as
		// sete esferas de Namek do mundo do dono e poria o Porunga pra dormir mais 44 h -- calada,
		// porque nada nesta bancada fala de esferas.
		// ============================================================================================================
		var setsGuardados = new List<SetDeEsferas>(_sets);
		var esferasGuardadas = new List<Esfera>(_esferas);

		try
		{
			Checa("(montagem) quem roda a bancada esta vivo -- senao o 'servidor vazio' congela tudo",
				  AlguemOnline());

			MedirAFuria(Checa);
			MedirARampa(Checa, pl);
			MedirASaidaEOAusente(Checa, pl);
			MedirASequencia(Checa);
			MedirOReinicio(Checa);
			MedirOReinicioDoDestruido(Checa, pl);
			MedirOServidorVazio(Checa);
			MedirOCommit(Checa, pl);
			MedirOPlanetaVazio(Checa, pl);
			MedirOProcedural(Checa, pl);
			MedirOsConsumidores(Checa);
			MedirOVerb(Checa, pl);
			MedirOsGates(Checa, pl);
			MedirODerrubarComKi(Checa, pl);

			// ============================ E AGORA A SEGUNDA METADE: "COMO SABEMOS QUE ISSO NAO ESTA MENTINDO?" ============================
			// As treze familias acima dizem que o sistema faz o que o dono pediu. As nove de baixo
			// (`GameServer.DestruicaoProva.cs`) provam que elas SABEM FICAR VERMELHAS, contam a formula
			// no fonte, medem a igualdade dano/vida nas duas pontas e varrem o sigilo do K5 pelo fio.
			//
			// **Dentro do palco**: elas matam a Terra, Namek, Vegeta e Arlia dezenas de vezes.
			// ==========================================================================================================================
			// O `MatouAqui` so e preenchido no `Dispose`, entao ele e lido DEPOIS do escopo -- ler
			// dentro daria zero e a checagem abaixo viraria uma frase.
			PalcoDeMortes palco = PalcoDeMortesDeBancada();
			try
			{
				(int okProvas, int falhasProvas, int cegos, _) = RodarAsProvasDaMorte(pl);
				ok += okProvas;
				falhou += falhasProvas + cegos;
			}
			finally { palco.Dispose(); }

			// ============================ O QUE SE MEDE E O QUE O PALCO BARROU, E NAO O QUE SOBROU ============================
			// `MatouAqui` compara o registro do fim com a foto do comeco -- e as provas limpam o
			// registro sozinhas entre familias, entao ele sai ZERO mesmo depois de matarem a Terra
			// dezenas de vezes (foi o que a primeira rodada mediu). O numero honesto e o das
			// GRAVACOES BARRADAS: cada uma e uma linha de producao que teria reescrito o
			// `planetas-mortos.json` do dono, contada no instante em que ela rodou.
			// ==============================================================================================================
			provasMataram = palco.EscritasBarradas;

			// O NUMERO VAI NO NOME e nao no detalhe: o `Checa` so imprime o detalhe quando a checagem
			// REPROVA, e este numero e justamente o que se quer ler quando ela passa.
			Checa($"**O PALCO BARROU {provasMataram} GRAVACOES DO LIVRO DOS MORTOS** -- cada uma teria "
				  + "reescrito o save do dono (palco que nunca cobre nada e indistinguivel de palco nenhum)",
				  provasMataram > 0,
				  $"{provasMataram} gravacao(oes) do livro dos mortos barradas -- "
				  + "cada uma teria reescrito o save do dono");
		}
		finally
		{
			_mortos.Limpar();
			foreach (EstadoDaMorte e in guardado) _mortos.Por(e);
			_proximoTremor.Clear();
			_cargaDoPlanetDestroy.Clear();

			// AS FERIDAS DE MUNDO TAMBEM. Elas nao vao pro disco, mas ficariam no servidor em
			// memoria -- e a familia (K) abre a cobaia varias vezes ate quase o fim.
			_feridasDeMundo.Clear();
			_faleiDoMundo.Clear();
			SalvarPlanetasMortos();
			MandarMortosPraTodos();

			// OS SETS VOLTAM DEPOIS DO REGISTRO DOS MORTOS, e a ordem e a mesma razao de sempre: com o
			// livro ja devolvido (Namek viva), o `SalvarEsferas` grava o Porunga certo no lugar certo.
			_sets.Clear(); _sets.AddRange(setsGuardados);
			_esferas.Clear(); _esferas.AddRange(esferasGuardadas);
			SalvarEsferas();

			// O ARQUIVO DO DONO VOLTA AO QUE ERA -- ver a fotografia la em cima. `Delete` aqui nao e
			// apagar dado dele: e desfazer um arquivo que ESTA bancada acabou de criar.
			try
			{
				if (livroDeAntes != null) System.IO.File.WriteAllBytes(caminhoDoLivro, livroDeAntes);
				else if (System.IO.File.Exists(caminhoDoLivro)) System.IO.File.Delete(caminhoDoLivro);
			}
			catch (Exception ex) { GD.PushWarning($"[bancada] nao deu pra devolver o livro dos mortos: {ex.Message}"); }

			pl.Ficha.isVillain = viloGuardado;
			pl.Ficha.BP = bpGuardado;
			pl.Ficha.HP = hpGuardado;
			pl.Ficha.Ki = kiGuardado;
			pl.Ficha.dead = mortoGuardado;
			pl.Ficha.KO = koGuardado;
			pl.Ficha.Planetgrav = gravGuardada;
			pl.Livro.Carregar(skillsGuardadas);
			pl.Livro.MarcosLivres = marcosGuardados;
			pl.Livro.MarcosTotais = totaisGuardados;
			pl.Ficha.Tick(agoraMs: NowMs());
			if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);

			// E O SAVE VOLTA JUNTO. Sem isto, o BP de 1e12 e o bit de vilao que a bancada usou
			// ficariam no disco -- e a rodada seguinte comecaria de um personagem que ela mesma
			// inventou, que e como ela se enganou uma vez.
			Persistir(pl);

			GD.Print($"===== BANCADA DA DESTRUICAO: {ok} OK, {falhou} FALHA =====\n");
		}
	}

	/// <summary>O planeta cobaia. Arlia: pre-feito, no manifesto, e ninguem nasce nele por padrao.</summary>
	private static ZoneKey Cobaia => ZoneKey.Premade("Arlia");

	// =====================================================================
	// 12. A FURIA -- **UMA formula, dois consumidores**
	// =====================================================================
	/// <summary>
	/// ============================ O QUE SO DAQUI SE RESPONDE ============================
	/// O dono foi explicito: *"dano de quando ele explode = vida do planeta"*. Uma formula, dois
	/// consumidores. As perguntas que uma checagem de aritmetica sozinha NAO responde:
	///
	///   * as duas ENTRADAS sao lidas do mundo de verdade (o manifesto e o `planetas.json`) e nao de
	///     um padrao? Este e o "campo morto" que este projeto ja pagou: se o catalogo falhasse, tudo
	///     cairia em 500 tiles / gravidade 1 **calado**, e a formula ficaria verde medindo constantes;
	///   * a resposta existe pra um mundo GERADO que nao esta carregado? Ela precisa existir, porque
	///     quem atira num planeta do espaco nunca pousou nele;
	///   * as duas populacoes (7 pre-feitos e infinitos gerados) compartilham escala? Uma formula
	///     calibrada pra Terra/Vegeta que tornasse o gerado pesado impossivel de derrubar quebraria a
	///     metade "vida" do pedido sem tocar na metade "dano" -- e so apareceria em teste com gente de
	///     BP variado, ou seja nunca.
	/// ================================================================================
	/// </summary>
	private void MedirAFuria(Checagem Checa)
	{
		// ---- as entradas vem do mundo, e nao de um padrao ----
		(int ladoTerra, double gravTerra) = MedidasDoPlaneta(ZoneKey.Premade("Earth"));
		(int ladoVegeta, double gravVegeta) = MedidasDoPlaneta(ZoneKey.Premade("Vegeta"));
		(int ladoIcer, double gravIcer) = MedidasDoPlaneta(ZoneKey.Premade("Icer"));

		Checa("o LADO de um pre-feito e lido do manifesto (500x500, medido nos 26 mapas)",
			  ladoTerra == 500 && ladoVegeta == 500 && ladoIcer == 500,
			  $"Terra {ladoTerra}, Vegeta {ladoVegeta}, Icer {ladoIcer}");

		// AS TRES GRAVIDADES, e nao uma: a do `Icer` so bate se o APELIDO do catalogo estiver vivo
		// ("Icer" -> "Icer Planet"), e aquele apelido ja custou 15x de gravidade calada uma vez.
		Checa("a GRAVIDADE vem do planetas.json -- inclusive pelo apelido (Icer -> Icer Planet)",
			  gravTerra == 1 && gravVegeta == 10 && gravIcer == 15,
			  $"Terra {gravTerra}, Vegeta {gravVegeta}, Icer {gravIcer}");

		// ---- e a resposta existe pra um mundo GERADO que ninguem carregou ----
		var geradoLeve = ZoneKey.Procedural("bancada-leve", SeedDeGravidade(1));
		var geradoPesado = ZoneKey.Procedural("bancada-pesado", SeedDeGravidade(80));
		(int ladoLeve, double gravLeve) = MedidasDoPlaneta(geradoLeve);
		(int ladoPesado, double gravPesado) = MedidasDoPlaneta(geradoPesado);

		Checa("**um mundo GERADO responde sem estar carregado** (funcao pura da seed)",
			  ladoLeve >= MundoProcedural.LadoMinimo && ladoPesado <= MundoProcedural.LadoMaximo
			  && !_zonasGeradas.ContainsKey(geradoPesado.Hash),
			  $"leve {ladoLeve}/{gravLeve}, pesado {ladoPesado}/{gravPesado}");

		// ---- a tabela, que e o pedido literal ("mostre a tabela de valores") ----
		GD.Print("  --   A FURIA DOS PLANETAS QUE EXISTEM HOJE (dano da explosao == vida do planeta):");
		foreach (string nome in new[] { "Earth", "Namek", "Arconia", "Makyo_Star", "Arlia", "Vegeta", "Icer" })
		{
			(int l, double g) = MedidasDoPlaneta(ZoneKey.Premade(nome));
			double f = MortePlanetaria.Furia(l, g);
			GD.Print($"       {nome,-12} lado {l,4}  grav {g,5:0.##}  ->  furia {f,10:N0}"
				   + $"   (quem esta muito acima leva {f * CombatKnobs.BpModMin:0} por membro)");
		}
		foreach (double g in new double[] { 1, 15, 40, 80 })
		{
			int l = MundoProcedural.LadoDaGravidade(g);
			double f = MortePlanetaria.Furia(l, g);
			GD.Print($"       {"gerado g" + g,-12} lado {l,4}  grav {g,5:0.##}  ->  furia {f,10:N0}"
				   + $"   (quem esta muito acima leva {f * CombatKnobs.BpModMin:0} por membro)");
		}

		// ---- monotonia nos DOIS eixos ----
		Checa("mais GRAVIDADE, mais furia (com o mesmo tamanho)",
			  MortePlanetaria.Furia(500, 15) > MortePlanetaria.Furia(500, 2)
			  && MortePlanetaria.Furia(500, 2) > MortePlanetaria.Furia(500, 1));
		Checa("mais TAMANHO, mais furia (com a mesma gravidade)",
			  MortePlanetaria.Furia(1000, 5) > MortePlanetaria.Furia(500, 5)
			  && MortePlanetaria.Furia(500, 5) > MortePlanetaria.Furia(192, 5));

		// ---- as duas populacoes na mesma regua ----
		double furiaTerra = MortePlanetaria.Furia(ladoTerra, gravTerra);
		double furiaPesado = MortePlanetaria.Furia(MundoProcedural.LadoDaGravidade(80), 80);
		double furiaLeve = MortePlanetaria.Furia(MundoProcedural.LadoDaGravidade(1), 1);

		Checa("**AS DUAS POPULACOES COMPARTILHAM ESCALA**: do mundo mais leve ao mais pesado nao ha "
			  + "duas ordens de grandeza",
			  furiaPesado / furiaLeve < 100,
			  $"leque {furiaPesado / furiaLeve:0.0}x (leve {furiaLeve:N0}, pesado {furiaPesado:N0})");

		// A CALIBRAGEM, afirmada nos DOIS sentidos -- e sao elas que dao sentido ao `FuriaBase`:
		// os pre-feitos sao sobreviviveis por quem esta muito alem deles, e os gerados pesados nao.
		double pisoDoGap = CombatKnobs.BpModMin;
		double nocaute = 100 * (1 - Regras.LimiarQuebra);

		Checa("um SER MUITO ALEM DE ICER (o pre-feito mais pesado) sobrevive a ele -- machucado",
			  MortePlanetaria.Furia(ladoIcer, gravIcer) * pisoDoGap < nocaute,
			  $"{MortePlanetaria.Furia(ladoIcer, gravIcer) * pisoDoGap:0.0} de dano contra "
			  + $"{nocaute:0} que ja nocauteia");
		Checa("...e NINGUEM sobrevive ao mundo gerado mais pesado",
			  furiaPesado * pisoDoGap > nocaute,
			  $"{furiaPesado * pisoDoGap:0.0} de dano contra {nocaute:0}");

		// ---- e a metade que amarra os dois consumidores ----
		Checa("**A FURIA DA ZONA E A MESMA FUNCAO DO CORE** (o servidor nao tem uma segunda conta)",
			  Math.Abs(FuriaDoPlaneta(Cobaia)
					   - MortePlanetaria.Furia(MedidasDoPlaneta(Cobaia).Lado,
											   MedidasDoPlaneta(Cobaia).Gravidade)) < 1e-9);
		Checa("...e o velho `DanoDoCommit = 99` sumiu do Core (numero substituido se DELETA)",
			  typeof(MortePlanetaria).GetField("DanoDoCommit") == null);
	}

	/// <summary>
	/// Uma seed cuja `GravidadeDaSeed` cai na gravidade pedida (ou a mais proxima achada).
	///
	/// PROCURA em vez de cravar: a escada de gravidade e um hash, e uma seed escrita a mao aqui
	/// deixaria de valer no dia em que alguem mexesse no `SalGravidade` -- e a bancada passaria a
	/// medir outra gravidade sem dizer.
	/// </summary>
	private static ulong SeedDeGravidade(double alvo)
	{
		ulong melhor = 1;
		double erro = double.MaxValue;
		for (ulong s = 1; s < 20000; s++)
		{
			double g = MundoProcedural.GravidadeDaSeed(s);
			double d = Math.Abs(g - alvo);
			if (d >= erro) continue;
			erro = d; melhor = s;
			if (d == 0) break;
		}
		return melhor;
	}

	// =====================================================================
	// 13. A RAMPA -- e ela tem que ser CONSUMIDA, nao so escrita
	// =====================================================================
	/// <summary>
	/// ============================ O QUE SO DAQUI SE RESPONDE ============================
	/// *"quanto mais perto ta de explodir, mais intenso esses efeitos ficam"*. A armadilha nomeada
	/// deste projeto e exatamente esta: **uma bancada que mede o campo escrito em vez do mundo
	/// mudado**. Uma rampa perfeita no Core que nenhum efeito lesse ficaria 100% verde.
	///
	/// Entao a metade de cima daqui mede a CURVA (monotona, acelerando, continua entre o pavio e a
	/// explosao) e a de baixo mede os TRES CONSUMIDORES, rodando o tique de producao:
	///   * o CEU aperta (forca do clima medida na zona, no comeco e no fim);
	///   * o TREMOR encurta (intervalos reais entre dois abalos);
	///   * o CHAO cai mais (celulas por volta), sem passar do teto declarado;
	///   * as CRATERAS saem de verdade (contadas no FIO, pela escuta de decalques).
	/// ================================================================================
	/// </summary>
	private void MedirARampa(Checagem Checa, ServerPlayer pl)
	{
		double t = MortePlanetaria.SegundosDeExplosao;

		// ---- a curva ----
		double a0 = MortePlanetaria.Intensidade(FaseDaMorte.Explodindo, 4, t);
		double aMeio = MortePlanetaria.Intensidade(FaseDaMorte.Explodindo, 4, t / 2);
		double a1 = MortePlanetaria.Intensidade(FaseDaMorte.Explodindo, 4, 0);

		Checa("a agonia comeca no PISO (o mundo ja treme no segundo zero) e termina em 1",
			  Math.Abs(a0 - MortePlanetaria.PisoDaAgonia) < 1e-9 && Math.Abs(a1 - 1) < 1e-9,
			  $"{a0:0.000} -> {a1:0.000}");

		bool sobe = true;
		double antes = -1;
		for (int s = 0; s <= 310; s += 5)
		{
			double a = MortePlanetaria.Intensidade(FaseDaMorte.Explodindo, 4, t - s);
			if (a < antes - 1e-12) sobe = false;
			antes = a;
		}
		Checa("**A RAMPA NUNCA DESCE** nos 310 s", sobe);

		Checa("**E ELA ACELERA**: a segunda metade ganha mais que a primeira",
			  a1 - aMeio > aMeio - a0, $"1a metade +{aMeio - a0:0.000}, 2a metade +{a1 - aMeio:0.000}");

		// A CONTINUIDADE ENTRE OS DOIS CAMINHOS: o topo do pavio lento e exatamente o piso da
		// explosao. Sem isto o planeta ficaria visivelmente mais CALMO no instante em que a conta
		// regressiva dispara -- e ninguem ligaria uma coisa na outra olhando.
		double topoDoPavio = MortePlanetaria.Intensidade(
			FaseDaMorte.Morrendo, MortePlanetaria.UltimoEstagio, 0);
		Checa("**A AGONIA E CONTINUA** do pavio lento pra explosao (o topo de um e o piso do outro)",
			  Math.Abs(topoDoPavio - a0) < 1e-9, $"pavio {topoDoPavio:0.000} vs explosao {a0:0.000}");

		// ---- E AGORA OS CONSUMIDORES, no tique de producao ----
		_mortos.Limpar();

		// ============================ A PLATEIA PRECISA DE **TECLADO**, E ISSO CUSTOU UMA RODADA ============================
		// A primeira versao forjava um corpo sem dono como plateia, e as SEIS checagens abaixo caiam
		// com zero a zero. Nao havia defeito nenhum: `TremorDaExplosao` e
		// `QuebrarChaoPertoDosJogadores` pulam quem tem `Peer == null` -- de propria regra do DM
		// (*"why affect land that won't affect players?"*, `Area_Death.dm:59`) --, e um corpo forjado
		// nao tem como ter `Peer`. A bancada estava medindo a propria montagem.
		//
		// Entao a plateia e o JOGADOR DE VERDADE, que e o unico com teclado num servidor de bancada.
		// Ele volta pra onde estava no `finally`, e a agonia e ABORTADA antes do commit -- esta
		// familia mede a rampa, e nao o desfecho (isso e do `MedirOCommit`).
		// ==============================================================================================================
		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, Cobaia, PontoDeNascimento(Cobaia));

		ComecarDestruicao(Cobaia, 1, "bancada-rampa");

		// ============================ O CEU MEDIDO E O QUE **VIAJA**, e nao o derivado ============================
		// `ClimaAgora().Forca` passa pelo `Clima.De`, que deriva a entrada/saida suave do RELOGIO DO
		// MUNDO -- e esta bancada adianta o tique da destruicao a mao, sem mover aquele relogio. Ler
		// dali daria 0,00 nas duas pontas (foi o que a primeira rodada mediu) e a familia ficaria
		// vermelha por artefato de montagem.
		//
		// O que se quer saber e se **o ceu que o servidor manda** subiu: e o `ClimaForcado.Forca`, o
		// mesmo campo que entra no `S2C.Clima`. A checagem logo abaixo fecha a outra metade,
		// perguntando ao `Clima.De` se um teto maior de fato vira mais ceu na tela.
		// ====================================================================================================
		double CeuMandado() =>
			_climaForcado.TryGetValue(Cobaia.Hash, out ClimaForcado f) ? f.Forca : 0;

		double ceuNoComeco = CeuMandado();
		var intervalos = new List<double>();
		int celulasNoComeco = 0, celulasNoFim = 0, crateras = 0;

		// A ESCUTA DE DECALQUES E O FIO, e nao o argumento: um decalque termina num `Peer.Send`, e a
		// pergunta "a cratera SAIU?" so se responde ali. Ver `MandarDecalque`.
		var escuta = new List<(ulong Zona, Protocol.Decal Tipo, byte[] Fio)>();
		EscutaDeDecalques = escuta;

		// O CHAO CAIDO E CONTADO NO MUNDO, e nao num contador de bancada: `_cenarioCaido` e a lista
		// de producao que o `DerrubarCelula` alimenta e que o cliente espelha. Um campo proprio aqui
		// mediria a bancada.
		int CelulasCaidas() =>
			_cenarioCaido.TryGetValue(Cobaia.Name, out HashSet<(int X, int Y)>? h) ? h.Count : 0;

		double ultimoTremor = 0;

		try
		{
			// PARA EM 300 s, ANTES DO COMMIT: esta familia mede a RAMPA, e o desfecho e do
			// `MedirOCommit`. Deixar chegar ao fim aqui feriria e evacuaria o jogador de verdade no
			// meio da bancada -- e as familias seguintes o encontrariam morto no espaco.
			for (int s = 0; s < 300 && MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo; s++)
			{
				int cAntes = CelulasCaidas();
				TickDaDestruicao(1);
				int caiu = CelulasCaidas() - cAntes;

				if (caiu > 0 || escuta.Count > crateras)
				{
					if (ultimoTremor > 0) intervalos.Add(s - ultimoTremor);
					ultimoTremor = s;
				}

				if (s < 100) celulasNoComeco += caiu; else if (s >= 200) celulasNoFim += caiu;
				crateras = escuta.Count;
			}

			double ceuNoFim = CeuMandado();

			Checa("**O CEU APERTA**: a forca do clima que o servidor manda sobe ao longo da agonia",
				  ceuNoFim > ceuNoComeco + 0.1, $"{ceuNoComeco:0.00} -> {ceuNoFim:0.00}");
			Checa("...e ele termina no auge (o ultimo minuto e o pior)",
				  ceuNoFim > 0.9, $"{ceuNoFim:0.00}");

			// A OUTRA METADE: teto maior tem que virar CEU maior de verdade. Sem ela, `ApertarClima`
			// poderia estar escrevendo num campo que o `Clima.De` nao le -- que e literalmente a
			// familia de defeito que este projeto chama de "API sem consumidor".
			if (_climaForcado.TryGetValue(Cobaia.Hash, out ClimaForcado noAuge))
			{
				ClimaDoPlaneta ficha = ClimaDaZona(Cobaia);
				ulong sal = Clima.SalDaZona(Cobaia);
				double meio = noAuge.Ate - noAuge.Duracao / 2;   // longe das duas transicoes

				double comAuge = Clima.De(ficha, meio, sal, noAuge).Forca;
				double comPiso = Clima.De(ficha, meio, sal, noAuge with { Forca = ceuNoComeco }).Forca;

				Checa("...e o ceu APERTADO de fato desenha mais que o ceu do comeco (o `Clima.De` le)",
					  comAuge > comPiso + 0.1, $"{comPiso:0.00} -> {comAuge:0.00}");
			}

			// O TREMOR ENCURTA. As duas metades comparadas entre si -- um numero absoluto seria
			// refem do jitter de +-25%.
			if (intervalos.Count >= 6)
			{
				double primeiros = intervalos.Take(intervalos.Count / 2).Average();
				double ultimos = intervalos.Skip(intervalos.Count / 2).Average();
				Checa("**O TREMOR ENCURTA**: o intervalo medio cai da primeira pra segunda metade",
					  ultimos < primeiros, $"{primeiros:0.0}s -> {ultimos:0.0}s");
			}
			else Checa("(montagem) houve tremores suficientes pra medir a cadencia",
					   false, $"so {intervalos.Count} intervalos");

			Checa("**O CHAO CAI MAIS** no fim do que no comeco",
				  celulasNoFim > celulasNoComeco,
				  $"{celulasNoComeco} celulas nos primeiros 100 s, {celulasNoFim} nos ultimos 100");

			// O TETO, na unidade que paga a conta -- celulas por segundo, e nao "por volta".
			Checa("...e sem passar do teto declarado (celulas por SEGUNDO, na zona)",
				  celulasNoFim / 100.0 <= TetoDeCelulasPorSegundo * 1.35,
				  $"{celulasNoFim / 100.0:0.00}/s contra teto {TetoDeCelulasPorSegundo:0.00}/s");

			int crateraGrande = escuta.Count(e => e.Tipo == Protocol.Decal.CrateraGrande);
			Checa("**AS CRATERAS APARECEM DE VERDADE** -- o ramo `if(4)` do DM, que nunca rodou lá",
				  escuta.Count(e => e.Tipo == Protocol.Decal.Cratera) > 0,
				  $"{escuta.Count} decalques no fio");
			Checa("...e a CRATERA GRANDE so entra na segunda metade da agonia",
				  crateraGrande > 0, $"{crateraGrande} crateras grandes");
		}
		finally
		{
			EscutaDeDecalques = null;
			AbortarMorte(Cobaia, "bancada-rampa acabou");
			MoveToZone(pl.Id, voltaZona, voltaPos);
			ForcarClima(Cobaia, TipoDeClima.Limpo, 0);

			// O CENARIO DERRUBADO VOLTA JUNTO: esta familia abre dezenas de buracos no chao de Arlia,
			// e `_cenarioCaido` e persistido com o mundo. Sem esta linha, rodar a bancada uma vez
			// deixaria o planeta esburacado pra sempre.
			_cenarioCaido.Remove(Cobaia.Name);

			_mortos.Limpar();
			_proximoTremor.Clear();
		}
	}

	// =====================================================================
	// 14. AS DUAS SAIDAS QUE DAVAM PRA UM MUNDO QUE NAO EXISTE MAIS
	// =====================================================================
	/// <summary>
	/// ============================ O QUE SO DAQUI SE RESPONDE ============================
	/// O pedido do dono cobre *"todos q estao no planeta"*. Estes dois nao estao no planeta e mesmo
	/// assim dependiam dele, e os dois foram medidos como QUEBRADOS antes:
	///
	///   * quem estava numa zona de INTERIOR ancorada nele (Templo, Sala do Tempo, cavernas, o Selo)
	///     e ANDA na passagem de volta -- a saida e cravada no mapa e nao consultava a lista de
	///     mortos, entao ela depositava o corpo dentro do cadaver;
	///   * quem estava OFFLINE com o save apontando pra la -- a guarda de login pergunta *"o mapa
	///     existe?"*, e o mapa de um planeta destruido continua no manifesto.
	///
	/// **ELA NAO NASCE DENTRO DO ESTADO**: nos dois casos o corpo comeca do lado de fora e ENTRA pelo
	/// caminho de producao (o `Atravessar` de verdade; o `OndeEsteCorpoPodeAcordar` que o `Entrar`
	/// chama). E as duas metades sao afirmadas: com o planeta VIVO os dois caminhos tem que continuar
	/// funcionando normalmente -- senao o conserto teria trancado as portas do jogo inteiro.
	/// ================================================================================
	/// </summary>
	private void MedirASaidaEOAusente(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();

		// ---- (a) A PASSAGEM, com o destino VIVO ----
		ServerPlayer viajante = ForjarEm(ZoneKey.Premade("Lookout"), "Viajante", 1e6);
		var portaProArlia = new Jandirus.Core.World.Passagem
		{
			X = 0, Y = 0, Zona = Cobaia.Name, Nome = "teste",
			Dx = PontoDeNascimento(Cobaia).X, Dy = PontoDeNascimento(Cobaia).Y,
		};

		Atravessar(viajante, portaProArlia);
		Checa("(a outra metade) com o planeta VIVO, a passagem continua levando pra la",
			  viajante.Zone.Equals(Cobaia), $"{viajante.Zone.Name}");

		// ---- (b) A MESMA PASSAGEM, com o destino MORTO ----
		MoveToZone(viajante.Id, ZoneKey.Premade("Lookout"), Vec2.Zero);
		ComecarDestruicao(Cobaia, 1, "bancada-saida");
		for (int i = 0; i < 400 && MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);

		Checa("(montagem) a cobaia esta destruida", ZonaMorta(Cobaia));

		Atravessar(viajante, portaProArlia);
		Checa("**QUEM SAI DO TEMPLO PRA UM MUNDO MORTO VAI PRO ESPACO** e nao pro cadaver",
			  Espaco.EhEspaco(viajante.Zone), $"{viajante.Zone.Name}");

		PlanetaNoEspaco? disco = null;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (p.Nome == Cobaia.Name) { disco = p; break; }
		if (disco is { } d)
			Checa("...no lugar EXATO onde o planeta ficava (o X4 aplicado tarde, pelo funil de sempre)",
				  (viajante.Pos - d.Pos).Length <= d.Raio + 200,
				  $"{(viajante.Pos - d.Pos).Length:0} px do centro (raio {d.Raio:0})");

		// ---- (c) O AUSENTE que volta ----
		// Pela porta de producao (`OndeEsteCorpoPodeAcordar`, que e o que o `Entrar` chama), e nao
		// escrevendo a zona e conferindo a zona.
		ServerPlayer ausente = ForjarEm(Cobaia, "Ausente", 1e5);
		// O BERCO E UM DE PRODUCAO, e nao um montado a mao: `Berco` e um `record struct` com nove
		// campos (`PreFeito`, `K`, `Seed`, `Sx`/`Sy`...), e um `new Berco { Planeta = "Earth" }` sai com
		// `PreFeito = false` -- ou seja, o funil o trata como mundo GERADO e larga o corpo em ORBITA.
		// Foi o que a primeira rodada mediu ("acordou em Espaco"), e o defeito era da montagem.
		ausente.Berco = pl.Berco;
		ausente.Zone = Cobaia;

		OndeEsteCorpoPodeAcordar(ausente);
		Checa("**QUEM DESLOGOU NUM PLANETA QUE EXPLODIU NAO ACORDA DENTRO DO CADAVER**",
			  !ausente.Zone.Equals(Cobaia), $"acordou em {ausente.Zone.Name}");
		Checa("...e nao acorda no vacuo tambem (o berco, e nao o X4 tarde)",
			  !Espaco.EhEspaco(ausente.Zone), $"{ausente.Zone.Name}");

		// A OUTRA METADE: num planeta VIVO ele continua acordando onde deslogou. Sem esta linha, uma
		// guarda que mandasse TODO MUNDO pro berco ficaria verde.
		_mortos.Limpar();
		ausente.Zone = Cobaia;
		OndeEsteCorpoPodeAcordar(ausente);
		Checa("(a outra metade) num planeta VIVO ele acorda exatamente onde deslogou",
			  ausente.Zone.Equals(Cobaia), $"{ausente.Zone.Name}");

		Recolher(viajante);
		Recolher(ausente);
		_mortos.Limpar();
		_proximoTremor.Clear();
	}

	// =====================================================================
	// 1. A SEQUENCIA
	// =====================================================================
	private void MedirASequencia(Checagem Checa)
	{
		Checa("os quatro estagios do pavio sao 200/300/300/400 s (Area_Death.dm:30,34,39,44)",
			  MortePlanetaria.SegundosDoEstagio.Length == 4
			  && MortePlanetaria.SegundosDoEstagio[0] == 200
			  && MortePlanetaria.SegundosDoEstagio[1] == 300
			  && MortePlanetaria.SegundosDoEstagio[2] == 300
			  && MortePlanetaria.SegundosDoEstagio[3] == 400,
			  string.Join("/", MortePlanetaria.SegundosDoEstagio));

		Checa("...o que da 20 minutos de pavio",
			  Math.Abs(MortePlanetaria.PavioInteiro - 1200) < 0.001,
			  $"{MortePlanetaria.PavioInteiro}");

		Checa("a explosao sao 310 s (o `sleep(3100)`, e nao os 'five minutes' do comentario)",
			  Math.Abs(MortePlanetaria.SegundosDeExplosao - 310) < 0.001);

		Checa("a chance do limit_life e 25/50/75/100 por estagio",
			  MortePlanetaria.ChanceDeMorrerPct(0) == 25 && MortePlanetaria.ChanceDeMorrerPct(1) == 50
			  && MortePlanetaria.ChanceDeMorrerPct(2) == 75 && MortePlanetaria.ChanceDeMorrerPct(3) == 100);

		// ---- O PAVIO ANDANDO, PELO CODIGO DE PRODUCAO ----
		_mortos.Limpar();
		Checa("acender o pavio lento em Arlia funciona",
			  ComecarMorteLenta(Cobaia, 5000, "bancada"));
		Checa("...e reacender NAO cria um segundo pavio (o `if(death_proc_running) return`)",
			  !ComecarMorteLenta(Cobaia, 5000, "bancada"));

		EstadoDaMorte e = MorteDaZona(Cobaia)!;
		Checa("ele nasce no estagio 0", e is { Fase: FaseDaMorte.Morrendo, Estagio: 0 },
			  $"{e.Fase}/{e.Estagio}");

		// O RELOGIO E ADIANTADO PELO TIQUE DE PRODUCAO, um segundo por volta. Chamar `Faltam = 0` na
		// mao mediria o campo, e nao a maquina que o le -- a armadilha 1 da PARTE 3.
		for (int i = 0; i < 200 && e.Estagio == 0; i++) TickDaDestruicao(1);
		Checa("depois de 200 s ele sobe pro estagio 1 (a noite que nao acaba)", e.Estagio == 1,
			  $"estagio {e.Estagio}");

		Checa("...e o ceu de Arlia virou o da DESTRUICAO",
			  ClimaAgora(Cobaia).Tipo == TipoDeClima.Destruicao,
			  $"{Clima.Nome(ClimaAgora(Cobaia).Tipo)}");

		for (int i = 0; i < 300 && e.Estagio == 1; i++) TickDaDestruicao(1);
		Checa("300 s depois, estagio 2 -- e daqui pra frente o chao se abre", e.Estagio == 2);

		for (int i = 0; i < 700 && e.Fase == FaseDaMorte.Morrendo; i++) TickDaDestruicao(1);
		Checa("terminado o pavio, ele vira EXPLODINDO sozinho (o `goto destroy`)",
			  e.Fase == FaseDaMorte.Explodindo, $"{e.Fase}/{e.Estagio}");
		Checa("...e a explosao nasce com os 310 s inteiros na frente",
			  Math.Abs(e.Faltam - MortePlanetaria.SegundosDeExplosao) < 1.001, $"{e.Faltam:0.#}");
	}

	// =====================================================================
	// 2. O REINICIO -- a armadilha do original
	// =====================================================================
	/// <summary>
	/// ============================ A CHECAGEM QUE JUSTIFICA O ARQUIVO ============================
	/// No DM, `planet_dying` volta do disco e `planet_death_stage` nao -- e o `Ticker` da area re-arma
	/// o `Planet_Death` **do zero**. Um planeta com o pavio aceso reinicia a morte a cada boot, pra
	/// sempre, e o remendo foi uma faxina no `Boss_Events_Init` (`BossEvents.dm:966-971`).
	///
	/// A bancada faz o caminho inteiro: grava, **apaga o registro em memoria** (que e o que um boot
	/// faz), le do disco, e confere que o estagio e o relogio voltaram iguais. Sem o `Limpar()` no
	/// meio, ela leria o objeto que ja estava na mao e passaria com o disco vazio.
	/// ========================================================================================
	/// </summary>
	private void MedirOReinicio(Checagem Checa)
	{
		_mortos.Limpar();
		ComecarMorteLenta(Cobaia, 7777, "bancada-reinicio");
		for (int i = 0; i < 260; i++) TickDaDestruicao(1);   // entra no estagio 1 e anda 60 s dele

		EstadoDaMorte antes = MorteDaZona(Cobaia)!;
		int estagioAntes = antes.Estagio;
		double faltamAntes = antes.Faltam;
		double bpAntes = antes.BpDoAlgoz;

		Checa("antes do 'reinicio' ele esta no estagio 1, no meio do relogio",
			  estagioAntes == 1 && faltamAntes is > 0 and < 300,
			  $"estagio {estagioAntes}, faltam {faltamAntes:0.#}");

		SalvarPlanetasMortos();
		_mortos.Limpar();                 // <- isto E o reinicio
		Checa("...e o registro em memoria some (simulando o boot)", MorteDaZona(Cobaia) == null);

		CarregarPlanetasMortos();
		EstadoDaMorte? depois = MorteDaZona(Cobaia);

		Checa("**O PAVIO VOLTA DO DISCO** (nao virou planeta vivo)", depois != null);
		Checa("**E ELE NAO REINICIA**: o estagio voltou o MESMO, e nao 0 (o bug do Weather.dm:72-74)",
			  depois?.Estagio == estagioAntes, $"{depois?.Estagio} vs {estagioAntes}");
		Checa("...e o relogio do estagio tambem voltou de onde parou",
			  depois != null && Math.Abs(depois.Faltam - faltamAntes) < 0.001,
			  $"{depois?.Faltam:0.###} vs {faltamAntes:0.###}");
		Checa("...e o BP do algoz sobreviveu (e ele que decide quem morre no fim)",
			  depois != null && Math.Abs(depois.BpDoAlgoz - bpAntes) < 0.001);

		// E O PAVIO CONTINUA ANDANDO depois do boot -- carregar sem retomar seria um planeta
		// congelado a meio caminho, que e um modo de falha tao ruim quanto reiniciar.
		double antesDoTique = depois!.Faltam;
		TickDaDestruicao(1);
		Checa("...e ele CONTINUA andando depois do boot (nao congelou)",
			  depois.Faltam < antesDoTique, $"{depois.Faltam:0.#} vs {antesDoTique:0.#}");
	}

	// =====================================================================
	// 3. SERVIDOR VAZIO
	// =====================================================================
	/// <summary>
	/// AS DUAS METADES. A bancada roda com UM jogador online (quem a disparou), entao pra medir o
	/// servidor vazio ela **tira o `Peer` dele por dois tiques** -- o mesmo estado que um logout
	/// produz, pelo campo que o codigo de producao le (`AlguemOnline`).
	/// </summary>
	private void MedirOServidorVazio(Checagem Checa)
	{
		_mortos.Limpar();
		ComecarMorteLenta(Cobaia, 5000, "bancada-vazio");
		EstadoDaMorte e = MorteDaZona(Cobaia)!;

		var peers = new List<(ServerPlayer P, LiteNetLib.NetPeer Peer)>();
		foreach (ServerPlayer p in _players.Values)
			if (p.Peer is { } pe) { peers.Add((p, pe)); p.Peer = null; }

		double antes = e.Faltam;
		TickDaDestruicao(1);
		TickDaDestruicao(1);
		Checa("**SERVIDOR VAZIO ADIA O PAVIO**: o relogio nao andou",
			  Math.Abs(e.Faltam - antes) < 0.001, $"{e.Faltam:0.###} vs {antes:0.###}");

		// E A EXPLOSAO NAO ESPERA. Mesma fotografia, fase diferente.
		ComecarDestruicao(Cobaia, 5000, "bancada-vazio");
		EstadoDaMorte x = MorteDaZona(Cobaia)!;
		double antesX = x.Faltam;
		TickDaDestruicao(1);
		Checa("**MAS A EXPLOSAO NAO ESPERA NINGUEM**: uma vez comecada, ela vai ate o fim",
			  x.Faltam < antesX, $"{x.Faltam:0.###} vs {antesX:0.###}");

		foreach ((ServerPlayer p, LiteNetLib.NetPeer pe) in peers) p.Peer = pe;

		// E COM GENTE DE VOLTA, o pavio volta a andar -- senao a checagem acima ficaria verde com
		// um pavio que nao anda NUNCA, que e o "teto que nunca dispara" ao contrario.
		_mortos.Limpar();
		ComecarMorteLenta(Cobaia, 5000, "bancada-vazio-2");
		EstadoDaMorte v = MorteDaZona(Cobaia)!;
		double antesV = v.Faltam;
		TickDaDestruicao(1);
		Checa("...e com alguem online ele volta a andar (a guarda nao trava o pavio pra sempre)",
			  v.Faltam < antesV, $"{v.Faltam:0.###} vs {antesV:0.###}");
	}

	// =====================================================================
	// 4. O COMMIT
	// =====================================================================
	/// <summary>
	/// Tres corpos forjados na zona cobaia -- um FRACO, um FORTE e um NOCAUTEADO -- e o quarto e o
	/// jogador de verdade, que mede a evacuacao.
	/// </summary>
	private void MedirOCommit(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();

		// O HABITANTE (com `Papel`) e forte de proposito: e ele que prova que o `isNPC` do commit
		// nao tem checagem de BP nenhuma.
		ServerPlayer? habitante = ForjarHabitante("Cidadao", 999_999);

		// E OS TRES SEM `Papel` sao "jogadores" aos olhos do commit -- e so por isso a regra do BP
		// pode ser medida. Forjados sem `Peer` porque nao ha como forjar um `NetPeer`; o que separa
		// jogador de habitante nesta regra e o `Papel`, nao o teclado.
		ServerPlayer fraco = ForjarNaCobaia("Fraco", 100);
		ServerPlayer forte = ForjarNaCobaia("Forte", 1e11);
		ServerPlayer caido = ForjarNaCobaia("Caido", 100);
		ServerPlayer seguro = ForjarNaCobaia("Segurado", 100);

		caido.Combate!.Nocautear(120);

		// O SEGURO DA PORTA UNICA: se o commit matar este corpo, ele nao esta passando pelo
		// `CombatState.Morrer()` -- e a Aura of Destruction nao valeria contra o fim do mundo.
		int negacoes = 0;
		seguro.Combate!.NegarMorte = _ => { negacoes++; return true; };

		// ============================ O JOGADOR TEM QUE SOBREVIVER PRA SER EVACUADO ============================
		// A primeira versao desta bancada punha o jogador com `BP = 1` -- ou seja, ABAIXO do algoz --,
		// e as tres checagens de evacuacao caiam. Estavam certas: quem esta abaixo do algoz leva 99 em
		// cada membro e MORRE, e um morto nao evacua (`:132` do original pula quem ja esta no Outro
		// Mundo). O defeito era a montagem, nao a regra.
		//
		// A evacuacao e sobre quem SOBRA. Entao o jogador entra forte -- e assim ele mede as duas
		// coisas de uma vez: que o forte atravessa o fim do mundo, e que o mundo o cospe pro espaco.
		//
		// `Tick` e nao `Statify`: e o `PowerLevel()` que escreve `expressedBP`, e e o `expressedBP`
		// que o commit compara. Com so `Statify` o numero fica velho, e a bancada mediria a
		// comparacao errada sem nada acusando (a armadilha 6 da PARTE 3).
		// ================================================================================================
		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, Cobaia, PontoDeNascimento(Cobaia));
		pl.Ficha.BP = 1e11;
		pl.Ficha.Tick(agoraMs: NowMs());

		// ============================ A REGUA DEIXOU DE SER O ALGOZ E PASSOU A SER O PLANETA ============================
		// O DM decide quem morre por `expressedBP <= mexpressedBP`. O dono pediu dano *"baseado na
		// gravidade do planeta e tamanho dele"*, e as duas coisas nao convivem -- ver
		// `MortePlanetaria.DanoNoCorpo`. Entao a montagem daqui mudou junto: o que precisa ser
		// afirmado agora e que o FRACO esta abaixo do limiar DESTE CHAO e o FORTE muito acima dele.
		//
		// O `bpDoAlgoz` continua sendo passado (ele ainda e o pino que sobrevive ao reinicio e o nome
		// que entra no funil de derrota), mas nao decide mais dano nenhum -- e a checagem do forte,
		// logo abaixo, e justamente a que reprovaria se alguem religasse o crivo antigo.
		// =========================================================================================================
		(int ladoDaCobaia, double gravDaCobaia) = MedidasDoPlaneta(Cobaia);
		double furiaDaCobaia = MortePlanetaria.Furia(ladoDaCobaia, gravDaCobaia);
		double limiarDaCobaia = MortePlanetaria.BpExigido(gravDaCobaia);
		double bpDoAlgoz = fraco.Ficha.expressedBP + 1;

		Checa("(montagem) o fraco esta abaixo do limiar deste chao e o forte MUITO acima dele",
			  fraco.Ficha.expressedBP < limiarDaCobaia
			  && forte.Ficha.expressedBP > limiarDaCobaia * 1000
			  && pl.Ficha.expressedBP > limiarDaCobaia * 1000,
			  $"limiar {limiarDaCobaia:0} | fraco {fraco.Ficha.expressedBP:0} | "
			  + $"forte {forte.Ficha.expressedBP:0} | eu {pl.Ficha.expressedBP:0}");

		double vidaDoForteAntes = forte.Combate!.Corpo.Partes.Sum(p => p.Vida);

		ComecarDestruicao(Cobaia, bpDoAlgoz, "bancada-commit");
		Checa("a explosao poe o ceu de DESTRUICAO na zona",
			  ClimaAgora(Cobaia).Tipo == TipoDeClima.Destruicao);

		for (int i = 0; i < 400 && MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);

		EstadoDaMorte e = MorteDaZona(Cobaia)!;
		Checa("passados os 310 s, o planeta esta DESTRUIDO", e.Fase == FaseDaMorte.Destruido, $"{e.Fase}");
		Checa("...e ele entrou na lista de mortos (a `PlanetDisableList`)", ZonaMorta(Cobaia));

		Checa("**O HABITANTE MORRE MESMO SENDO MAIS FORTE QUE O ALGOZ** (o `isNPC` do :134 nao "
			  + "olha BP nenhum)",
			  habitante is { Ficha.dead: true },
			  habitante == null ? "sem molde 'cidadao' no npcs.json"
								: $"dead={habitante.Ficha.dead}, BP {habitante.Ficha.expressedBP:0}");

		Checa("o fraco morreu (a furia deste mundo inteira em cada membro)", fraco.Ficha.dead);
		Checa("o NOCAUTEADO morreu (Area_Death.dm:140-142)", caido.Ficha.dead);

		// ============================ AS DUAS METADES DO X3, E ELAS SAO O CORACAO DA MUDANCA ============================
		// **Sobreviver E ser ferido.** Afirmar so a primeira e o que a bancada media antes, e ela
		// ficaria verde com o dano DESLIGADO -- que e literalmente o modo de falha que o projeto chama
		// de "afirmacao de um lado so fica verde num sistema morto". Afirmar so a segunda deixaria
		// passar um planeta que mata todo mundo.
		// ==========================================================================================================
		double vidaDoForteDepois = forte.Combate!.Corpo.Partes.Sum(p => p.Vida);
		double perdaEsperada = MortePlanetaria.DanoNoCorpo(
			furiaDaCobaia, gravDaCobaia, forte.Ficha.expressedBP);

		Checa("**QUEM E MUITO MAIS FORTE QUE O PLANETA ATRAVESSA O FIM DO MUNDO**",
			  !forte.Ficha.dead,
			  $"BP {forte.Ficha.expressedBP:0} vs limiar {limiarDaCobaia:0}");
		Checa("...**MAS SAI MACHUCADO**: ninguem passa ileso por um mundo estourando",
			  vidaDoForteDepois < vidaDoForteAntes,
			  $"vida {vidaDoForteAntes:0} -> {vidaDoForteDepois:0}");
		// ============================ O NUMERO E O DA FORMULA, E NAO "ALGUM" DANO ============================
		// Medido num membro EXTERNO SEM FILHOS -- `Body.Ferir` escorre uma fracao do golpe pros
		// aninhados (e assim que o cerebro se fere), entao somar o corpo inteiro nao bate com
		// `dano x membros` e a checagem viraria uma tolerancia grande o bastante pra nao provar nada.
		// Num membro folha a conta e exata: `VidaMax - Vida == dano`.
		// ==================================================================================================
		var corpoDoForte = forte.Combate.Corpo;
		// ============================ "FOLHA" E MAIS ESTRITO DO QUE PARECE, E ISSO FOI MEDIDO ============================
		// A primeira versao pedia so "nao aninhado e sem filhos", e caiu: o **Reprodutor** perdeu
		// 25,456 quando a formula mandava 21,213 -- exatamente 1,2x, que e `1 + Regras.Propagacao`.
		// Ele nao e `Aninhado` (entao o laco do `EspalharDanoG3` bate nele diretamente), mas TEM
		// `Dono` (o Abdomen), entao ele leva a propagacao POR CIMA do golpe direto.
		//
		// O que mede a formula limpa e o membro **SEM DONO** -- ter filhos nao importa, porque a
		// propagacao so desce (o `Ferir` visita quem tem `Dono == alvo.Nome`, nunca o contrario).
		// Exigir "sem filhos" tambem, como a segunda versao fazia, nao sobrava membro NENHUM: no
		// corpo deste jogo todo externo carrega alguma coisa dentro (torso->abdomen, braco->mao).
		// ==========================================================================================================
		BodyPart? folha = null;
		foreach (BodyPart p in corpoDoForte.Partes)
		{
			if (p.Decepado || p.Aninhado || p.Dono != null) continue;
			folha = p;
			break;
		}

		Checa("...e o estrago dele e EXATAMENTE o que a formula do Core manda (uma conta, nao duas)",
			  folha is { } f2 && Math.Abs(f2.VidaMax - f2.Vida - perdaEsperada) < 0.01,
			  folha is { } f3 ? $"'{f3.Nome}' perdeu {f3.VidaMax - f3.Vida:0.000}, "
					+ $"formula manda {perdaEsperada:0.000}"
				: "nenhum membro folha");

		Checa("**A MORTE PASSA PELA PORTA UNICA**: o `NegarMorte` foi consultado...",
			  negacoes > 0, $"{negacoes} consultas");
		Checa("...e quem tem seguro NAO morreu no fim do mundo", !seguro.Ficha.dead);

		// ---- A EVACUACAO ----
		Checa("o jogador (mais forte que o algoz) sobreviveu ao fim do mundo", !pl.Ficha.dead);
		Checa("**O JOGADOR FOI EVACUADO PRO ESPACO**", Espaco.EhEspaco(pl.Zone), $"{pl.Zone.Name}");

		PlanetaNoEspaco? arlia = null;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (p.Nome == "Arlia") { arlia = p; break; }

		if (arlia is { } disco)
		{
			double d = (pl.Pos - disco.Pos).Length;
			Checa("...e FORA do raio do disco -- ninguem re-pousa no mesmo quadro",
				  d > disco.Raio, $"{d:0} px contra raio {disco.Raio:0}");
			Checa("...e o carimbo de chunk foi feito (senao a vizinhanca sairia duplicada)",
				  pl.ChunkAtual == ChunkId.De(pl.Pos));
		}

		// ---- E ELE NAO CONSEGUE VOLTAR ----
		// Sem esta checagem a evacuacao seria um empurraozinho de 90 px: o `TickDoEspaco` roda a
		// 30 Hz e o corpo esta a 90 px do disco.
		if (arlia is { } alvo)
		{
			pl.Pos = alvo.Pos;   // colado no centro do planeta morto
			TickDoEspaco(pl);
			Checa("**E ELE NAO CONSEGUE VOLTAR**: encostar num planeta morto nao pousa",
				  Espaco.EhEspaco(pl.Zone), $"{pl.Zone.Name}");
		}

		// `ServerPlayer?` no array: o habitante e nulo quando o `npcs.json` nao trouxe o molde
		// `cidadao`, e a checagem dele ja disse isso em voz alta la em cima.
		foreach (ServerPlayer? forjado in new ServerPlayer?[] { fraco, forte, caido, seguro, habitante })
			if (forjado != null) Recolher(forjado);
		MoveToZone(pl.Id, voltaZona, voltaPos);
	}

	/// <summary>
	/// Um corpo sem dono na zona cobaia, pelo caminho do clone/NPC de bancada. **Sem `Peer`** -- o
	/// que faz dele um habitante aos olhos do commit.
	/// </summary>
	private ServerPlayer ForjarNaCobaia(string nome, double bp) => ForjarEm(Cobaia, nome, bp);

	/// <summary>O mesmo corpo sem dono, em qualquer zona -- a bancada das sagas tambem precisa dele.</summary>
	private ServerPlayer ForjarEm(ZoneKey zona, string nome, double bp)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDoPlanetaDeTeste + _forjadosDoPlaneta++,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = PontoDeNascimento(zona),
			Conta = $"bancada_planeta_{nome}",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = bp },
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;

		// `PorNoMundo` chama `Statify` (os ATRIBUTOS) e nao `PowerLevel` (o PODER). Sem esta linha o
		// `expressedBP` nasce ZERO -- e o commit compara justamente `expressedBP <= mexpressedBP`,
		// entao a bancada mediria todo mundo, do fraco ao de um milhao, caindo no mesmo lado.
		novo.Ficha.Tick(agoraMs: NowMs());
		return novo;
	}

	/// <summary>
	/// UM HABITANTE de verdade na cobaia -- com `Papel`, que e o `isNPC` deste port.
	///
	/// Ele precisa do molde de producao: o `limit_life` e o commit perguntam pelo `Papel`, e um corpo
	/// forjado sem ele seria um jogador aos olhos das duas regras. Devolve nulo quando o `npcs.json`
	/// nao carregou -- e ai a checagem que depende dele diz isso em voz alta, em vez de passar.
	/// </summary>
	private ServerPlayer? ForjarHabitante(string nome, double bp) =>
		ForjarHabitanteEm(Cobaia, nome, bp);

	/// <summary>O habitante, em qualquer zona. Ver <see cref="ForjarHabitante"/>.</summary>
	private ServerPlayer? ForjarHabitanteEm(ZoneKey zona, string nome, double bp)
	{
		if (_moldes?.Get("cidadao") is not { } molde) return null;

		ServerPlayer c = ForjarEm(zona, nome, bp);
		c.Papel = new Jandirus.Core.Npc.PapelDeNpc(molde, (ulong)c.Id);
		return c;
	}

	/// <summary>Faixa de ids propria, longe da dos jogadores e da das outras bancadas.</summary>
	private const int IdBaseDoPlanetaDeTeste = 940_000;
	private int _forjadosDoPlaneta;

	// =====================================================================
	// 5. OS CONSUMIDORES ESQUECIDOS
	// =====================================================================
	/// <summary>
	/// ============================ AS TRES PORTAS QUE DESFARIAM O SISTEMA SOZINHAS ============================
	///   * **o POVOAMENTO**: a manutencao repovoa ate o alvo a cada 5 min. Sem o corte, os 40
	///     cidadaos que o `limit_life` matou voltam sozinhos e o planeta morto fica cheio de gente.
	///   * **o BERCO**: a saga 1 destroi **Vegeta**, o berco dos Saiyajin. Sem o corte, todo Saiyajin
	///     que nascesse ou morresse acordaria num planeta que nao existe.
	///   * **o POUSO**: ja medido no commit.
	/// ==================================================================================================
	/// </summary>
	private void MedirOsConsumidores(Checagem Checa)
	{
		_mortos.Limpar();
		ComecarDestruicao(ZoneKey.Premade("Vegeta"), 1, "bancada-consumidores");
		MorteDaZona(ZoneKey.Premade("Vegeta"))!.Fase = FaseDaMorte.Destruido;

		// ============================ A FILA PRECISA ESTAR VAZIA PRA MEDIR A FILA ============================
		// A primeira versao desta bancada nao limpava, e a checagem caiu com "40 na fila". Ela estava
		// medindo o povoamento do BOOT: a manutencao inicial enfileira os 148 cidadaos do plano e o
		// dreno solta 1 por tique -- no primeiro login a fila ainda esta cheia. Ou seja, ela via os
		// 40 de Vegeta que o servidor pediu ANTES de o planeta morrer e chamava isso de repovoamento.
		//
		// E a mesma familia de erro que a bancada do povoamento ja tinha registrado ("a fila envelhece
		// e o mundo anda"): quem mede fila tem que saber de onde cada item veio.
		// ================================================================================================
		_filaDoPovoamento.Clear();

		// ---- POVOAMENTO ----
		int antes = ContarHabitantes("Vegeta", "cidadao");
		Manutencao();
		int naFila = _filaDoPovoamento.Count(f => f.Item2.Hash == ZoneKey.Premade("Vegeta").Hash);
		int outros = _filaDoPovoamento.Count - naFila;
		Checa("**PLANETA MORTO NAO SE REPOVOA**: a manutencao nao enfileirou ninguem em Vegeta",
			  naFila == 0, $"{naFila} na fila (havia {antes} vivos)");

		// E O CORTE E SO DE VEGETA. Sem esta metade, um bug que desligasse o povoamento INTEIRO
		// deixaria a checagem acima verde -- o "teto que nunca dispara" na forma inversa.
		Checa("...mas os outros planetas continuam se povoando (o corte e so do morto)",
			  outros > 0, $"{outros} enfileirados fora de Vegeta");

		// ---- BERCO ----
		var bercoSaiyajin = new Jandirus.Core.Races.Berco
		{
			Planeta = "Vegeta",
			PreFeito = true,
		};
		(ZoneKey zona, _) = DestinoDoBerco(bercoSaiyajin);
		Checa("**NINGUEM NASCE NUM CADAVER**: o berco Saiyajin (Vegeta) foi desviado",
			  !zona.Equals(ZoneKey.Premade("Vegeta")), $"caiu em {zona.Name}");
		Checa("...e o desvio caiu num planeta VIVO", !ZonaMorta(zona), zona.Name);

		// ---- E O DESFAZER ----
		Checa("restaurar tira da lista (o `Restaurar_Planeta`, a unica volta que existe)",
			  RessuscitarPlaneta(ZoneKey.Premade("Vegeta")));
		Checa("...e Vegeta volta a ser um berco valido",
			  DestinoDoBerco(bercoSaiyajin).Zona.Equals(ZoneKey.Premade("Vegeta")));

		// A FILA E LIMPA: a manutencao acima pode ter enfileirado corpos de OUTROS planetas, e
		// deixa-los nascer encheria o mundo do dono por causa da bancada.
		_filaDoPovoamento.Clear();
	}

	// =====================================================================
	// 6. O VERB DO JOGADOR
	// =====================================================================
	private void MedirOVerb(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();
		_cargaDoPlanetDestroy.Clear();

		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, Cobaia, PontoDeNascimento(Cobaia));

		// ============================ O CORPO PRECISA ESTAR DE PE PRA APERTAR O BOTAO ============================
		// O verb recusa quem esta morto ou nocauteado, e o bloco anterior acabou de explodir um planeta
		// debaixo deste jogador. Sem esta linha, TODAS as checagens do verb caiam -- e caiam pela razao
		// certa (um morto nao destroi planeta), medindo a montagem em vez da regra.
		//
		// `Tick` e nao `Statify` pelo mesmo motivo do bloco do commit: e ele que escreve `expressedBP`,
		// que e o numero que o verb compara contra `10000 * Planetgrav`.
		// ==================================================================================================
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate?.Corpo.Curar(1e9);
		pl.Combate?.SincronizarVida();
		pl.Ficha.BP = 1e12;
		pl.Ficha.Tick(agoraMs: NowMs());
		pl.Ficha.Ki = 99_999;

		// ---- 1. SO VILAO ----
		pl.Ficha.isVillain = false;

		PlanetDestroy(pl);
		Checa("**SEM SER VILAO NAO SAI** (o `if(!usr.isVillain)` de Planets.dm:323)",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id));

		// ---- 2. O CUSTO ----
		pl.Ficha.isVillain = true;
		double kiGuardado = pl.Ficha.Ki;
		pl.Ficha.Ki = MortePlanetaria.KiDaDestruicao - 1;
		PlanetDestroy(pl);
		Checa("...nem com 999 de Ki (o `PDESTROY_KI_COST 1000`)",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id));

		pl.Ficha.Ki = kiGuardado;
		PlanetDestroy(pl);
		Checa("vilao com Ki e BP sobrando COMECA a carga",
			  _cargaDoPlanetDestroy.ContainsKey(pl.Id));
		Checa("...e os 1000 de Ki saem NA HORA (o `usr.Ki -= ...` do :338, antes da confirmacao)",
			  Math.Abs(kiGuardado - pl.Ficha.Ki - MortePlanetaria.KiDaDestruicao) < 0.001,
			  $"gastou {kiGuardado - pl.Ficha.Ki:0}");

		// ---- 3. O NOCAUTE INTERROMPE ----
		pl.Combate!.Nocautear(120);
		TickDaDestruicao(1);
		Checa("**NOCAUTEA-LO NA JANELA INTERROMPE** e o planeta nao e condenado",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id) && MorteDaZona(Cobaia) == null);

		// ---- 4. A CARGA INTEIRA ACENDE A DESTRUICAO ----
		pl.Ficha.KO = false;
		pl.Ficha.Ki = 99_999;
		PlanetDestroy(pl);
		for (int i = 0; i < (int)MortePlanetaria.SegundosDeCarga + 2; i++) TickDaDestruicao(1);
		Checa($"esperados os {MortePlanetaria.SegundosDeCarga:0} s de carga, a destruicao comeca",
			  MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo,
			  $"{MorteDaZona(Cobaia)?.Fase}");

		Checa("abortar antes do fim devolve o planeta", AbortarMorte(Cobaia, "bancada"));
		Checa("...e ele nao esta mais condenado", MorteDaZona(Cobaia) == null);

		MoveToZone(pl.Id, voltaZona, voltaPos);
	}

	// =====================================================================
	// 7. NINGUEM DENTRO -- e a explosao para na BORDA DA ZONA
	// =====================================================================
	/// <summary>
	/// ============================ O FIM DO MUNDO SEM PLATEIA ============================
	/// O bloco 4 mede o commit **com gente dentro**. Este mede a outra metade, e ela nao e a mesma
	/// afirmacao ao contrario: um commit que nao encontra ninguem percorre um caminho diferente --
	/// nenhum `EspalharDanoG3`, nenhum `Morrer`, nenhum `EvacuarParaOEspaco` -- e mesmo assim tem que
	/// chegar ao mesmo lugar. **A lista e o efeito; a plateia nao e.**
	///
	/// E ELA TEM DENTES POR CAUSA DA TESTEMUNHA DISTANTE: um corpo FRACO (abaixo do algoz, portanto
	/// alcancavel pela regra do `<=`) parado **noutro planeta**. Se o commit varresse `_players` em vez
	/// da lista da zona, ele levaria 99 em cada membro na Terra por causa de uma explosao em Arlia --
	/// e o resto da bancada nao notaria, porque todo o resto dela mede gente que ESTA na cobaia.
	///
	/// A COBAIA E ESVAZIADA DE PROPOSITO. Arlia tem 12 cidadaos no plano de povoamento
	/// (`npcs.json`), e "ninguem dentro" precisa ser um FATO e nao uma sorte de cronometragem: no
	/// primeiro login a fila do povoamento ainda esta cheia e a zona esta vazia por acaso -- medir isso
	/// seria medir o relogio do boot. Os corpos voltam pela `Manutencao`, que e a mesma porta que
	/// enche o mundo no boot.
	/// ================================================================================
	/// </summary>
	private void MedirOPlanetaVazio(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();

		foreach (ServerPlayer c in ZoneList(Cobaia.Hash).ToList()) Recolher(c);
		Checa("(montagem) a cobaia esta VAZIA -- nenhum corpo, nem habitante",
			  ZoneList(Cobaia.Hash).Count == 0, $"{ZoneList(Cobaia.Hash).Count} corpo(s)");

		// A TESTEMUNHA DISTANTE: fraca, viva e NOUTRO PLANETA.
		ZoneKey longe = ZoneKey.Premade("Earth");
		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate?.Corpo.Curar(1e9);
		pl.Combate?.SincronizarVida();
		pl.Ficha.BP = 100;
		pl.Ficha.Tick(agoraMs: NowMs());

		double bpDoAlgoz = pl.Ficha.expressedBP * 1000;
		double hpAntes = pl.Ficha.HP;
		Checa("(montagem) a testemunha esta ABAIXO do algoz -- ou seja, o `<=` do commit a alcancaria",
			  pl.Ficha.expressedBP <= bpDoAlgoz,
			  $"testemunha {pl.Ficha.expressedBP:0} vs algoz {bpDoAlgoz:0}");

		ComecarDestruicao(Cobaia, bpDoAlgoz, "bancada-vazio-total");
		for (int i = 0; i < 400 && MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);

		Checa("**UM PLANETA SEM NINGUEM EXPLODE ATE O FIM** (o commit nao depende de achar corpo)",
			  MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Destruido, $"{MorteDaZona(Cobaia)?.Fase}");
		Checa("...e entra na lista de mortos do mesmo jeito -- a lista e o efeito, nao a plateia",
			  ZonaMorta(Cobaia));

		Checa("**E A EXPLOSAO PARA NA BORDA DA ZONA**: a testemunha na Terra nao levou um arranhao",
			  !pl.Ficha.dead && Math.Abs(pl.Ficha.HP - hpAntes) < 0.001,
			  $"HP {pl.Ficha.HP:0.##} (era {hpAntes:0.##}), dead={pl.Ficha.dead}");
		Checa("...e nao foi evacuada: quem nao estava no planeta nao e jogado no vacuo",
			  pl.Zone.Equals(longe), $"{pl.Zone.Name}");

		MoveToZone(pl.Id, voltaZona, voltaPos);
	}

	// =====================================================================
	// 8. O PLANETA GERADO -- e a prova de que a chave e a SEED
	// =====================================================================
	/// <summary>
	/// ============================ "O PLANETA PROCEDURAL RENASCE DESTRUIDO" ============================
	/// A frase e do original (o planeta volta destruido se o nome estiver na `PlanetDisableList`), e
	/// aqui ela vale mais forte: um mundo gerado **nao existe** entre um boot e outro. Nao ha arquivo,
	/// nao ha zona viva, nao ha nada -- ele e refeito do zero a partir da seed toda vez que alguem
	/// olha. Se o registro de mortos nao o alcancasse, destruir um mundo gerado seria destruir a copia
	/// em memoria e nada mais.
	///
	/// ============================ E A CHECAGEM DO GEMEO E O ARQUIVO INTEIRO NUMA LINHA ============================
	/// `ChaveDePlaneta` existe porque o nome procedural **nao e unico**: ele sai de
	/// `{bioma}-{|Sx|%1000}{|Sy|%1000}{k}`, o `%1000` colide e a concatenacao e ambigua. Chavear por
	/// nome -- que e literalmente o que o DM faz (`PlanetDisableList += P.planetType`) -- mataria
	/// planetas do outro lado da galaxia, **calados**.
	///
	/// Entao esta familia destroi um mundo gerado e pergunta de um IRMAO de mesmo nome e outra seed se
	/// ele esta vivo. E a unica checagem da bancada que fica vermelha se alguem "simplificar" a chave.
	/// ==========================================================================================================
	/// </summary>
	private void MedirOProcedural(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();

		if (AcharPlanetaGerado(SeedDoUniverso) is not { } p)
		{
			Checa("ha um planeta GERADO no mapa do universo pra medir", false,
				  "nenhum procedural nas chunks varridas");
			return;
		}

		var zona = ZoneKey.Procedural(p.Nome, p.Seed);
		Checa($"(montagem) '{p.Nome}' e gerado, e a chave dele e a SEED e nao o nome",
			  !p.Premade && ChaveDePlaneta.Da(zona)?.Texto == $"#{p.Seed}",
			  $"{ChaveDePlaneta.Da(zona)?.Texto}");

		ComecarDestruicao(zona, 1e9, "bancada-procedural");
		for (int i = 0; i < 400 && MorteDaZona(zona)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);

		Checa("um mundo GERADO tambem morre (a lista nao e so dos sete pre-feitos)",
			  ZonaMorta(zona), $"{MorteDaZona(zona)?.Fase}");

		// ---- O REINICIO ----
		SalvarPlanetasMortos();
		_mortos.Limpar();
		Checa("(o registro em memoria some -- isto E o boot)", !ZonaMorta(zona));
		CarregarPlanetasMortos();

		// E A RE-ENUMERACAO: o corpo e refeito do ZERO a partir da seed do universo, pelo MESMO
		// caminho que a carta estelar e o pouso usam. Nao ha estado carregado aqui -- so a funcao.
		PlanetaNoEspaco? denovo = null;
		foreach (PlanetaNoEspaco q in Espaco.PorPerto(SeedDoUniverso, ChunkId.De(p.Pos)))
			if (q.Seed == p.Seed) { denovo = q; break; }

		Checa("...e o corpo volta a ser enumerado da seed (a funcao pura nao guarda rancor)",
			  denovo != null);
		Checa("**O PLANETA GERADO RENASCE DESTRUIDO**: enumerado de novo depois do boot, ja vem morto",
			  denovo is { } d && PlanetaMorto(d));

		// ---- O GEMEO DE MESMO NOME ----
		var gemeo = ZoneKey.Procedural(p.Nome, p.Seed ^ 0xA5A5_A5A5_A5A5_A5A5UL);
		Checa("**E O IRMAO DE MESMO NOME E OUTRA SEED CONTINUA VIVO** -- chavear por nome (o "
			+ "`PlanetDisableList` do DM) mataria os dois",
			  !ZonaMorta(gemeo), $"{ChaveDePlaneta.Da(gemeo)?.Texto}");

		// ---- E NAO SE POUSA NO CADAVER GERADO ----
		// O filtro do `TickDoEspaco` e o MESMO dos pre-feitos; sem esta linha o "renasce destruido"
		// seria um bit no arquivo sem consequencia nenhuma no jogo.
		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, ZonaDoEspaco, p.Pos);
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		TickDoEspaco(pl);
		Checa("...e encostar nele no espaco NAO pousa (o `testPlanetbump` vale pro gerado tambem)",
			  Espaco.EhEspaco(pl.Zone), $"{pl.Zone.Name}");

		MoveToZone(pl.Id, voltaZona, voltaPos);
	}

	/// <summary>
	/// O PRIMEIRO PLANETA GERADO QUE APARECER, varrendo chunks longe dos pre-feitos.
	///
	/// A varredura e por CELULA de sistema e nao aleatoria: 37,6% das celulas nao tem estrela nenhuma
	/// (<see cref="Sistemas.VaziosPor256"/>), entao olhar uma chunk so daria nulo com frequencia e a
	/// familia inteira sumiria num "nao havia o que medir".
	///
	/// A SEMENTE ENTRA POR PARAMETRO desde que ela deixou de ser constante: quem chama passa a
	/// `SeedDoUniverso` **deste** servidor, porque o corpo achado aqui vai ser destruido no mundo em
	/// que o servidor esta de pe. Ler uma constante aqui dentro acharia planeta noutro universo -- e
	/// a bancada mediria a morte de um planeta que nao existe.
	/// </summary>
	private static PlanetaNoEspaco? AcharPlanetaGerado(ulong semente)
	{
		for (int i = 1; i <= 600; i++)
			foreach (PlanetaNoEspaco p in Espaco.PorPerto(semente, new ChunkId(i * 37, i * 53)))
				if (!p.Premade) return p;
		return null;
	}

	// =====================================================================
	// 9. O DESTRUIDO SOBREVIVE AO REINICIO
	// =====================================================================
	/// <summary>
	/// ============================ O BLOCO 2 MEDE O PAVIO; ESTE MEDE O CADAVER ============================
	/// Sao estados diferentes e caminhos de save diferentes: `Morrendo` carrega estagio e relogio,
	/// `Destruido` e um estado FINAL, com `Faltam = 0` e sem nada pra retomar. Um save que so soubesse
	/// gravar o pavio deixaria o bloco 2 verde e **ressuscitaria todo planeta destruido a cada boot** --
	/// que e o pior modo de falha deste sistema inteiro, porque ele apaga trabalho de saga.
	///
	/// E a ultima checagem e a que fecha o par com o bloco 2: **o tique nao mexe mais nele.** No DM o
	/// `Ticker` da area le `planet_dying` e re-arma a morte; aqui um planeta ja destruido tem que ser
	/// invisivel pro tique, ou o cadaver "morreria" de novo todo boot.
	/// ================================================================================================
	/// </summary>
	private void MedirOReinicioDoDestruido(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();
		ComecarDestruicao(Cobaia, 4321, "bancada-reinicio-destruido");
		for (int i = 0; i < 400 && MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);
		Checa("(montagem) a cobaia chegou ao estado DESTRUIDO", ZonaMorta(Cobaia));

		SalvarPlanetasMortos();
		_mortos.Limpar();
		Checa("...e o registro em memoria some (isto E o boot)", !ZonaMorta(Cobaia));

		CarregarPlanetasMortos();
		EstadoDaMorte? e = MorteDaZona(Cobaia);

		Checa("**O PLANETA CONTINUA MORTO DEPOIS DE REINICIAR** (a `PlanetDisableList` persistente)",
			  ZonaMorta(Cobaia));
		Checa("...e volta como DESTRUIDO, e nao 'morrendo outra vez'",
			  e?.Fase == FaseDaMorte.Destruido, $"{e?.Fase}");
		Checa("...e o BP do algoz veio junto (o cadaver guarda quem o matou)",
			  e != null && Math.Abs(e.BpDoAlgoz - 4321) < 0.001, $"{e?.BpDoAlgoz:0}");

		// ---- E O TIQUE NAO MEXE MAIS NELE ----
		for (int i = 0; i < 10; i++) TickDaDestruicao(1);
		Checa("**E O TIQUE NAO O RESSUSCITA NEM O MATA DE NOVO**: morto e estado final, nao relogio",
			  MorteDaZona(Cobaia) is { Fase: FaseDaMorte.Destruido, Faltam: <= 0 },
			  $"{MorteDaZona(Cobaia)?.Fase}/{MorteDaZona(Cobaia)?.Faltam:0.#}");

		// ---- E OS CONSUMIDORES CONTINUAM VENDO O CADAVER DEPOIS DO BOOT ----
		// Sem isto, "continua morto" seria uma linha no arquivo sem efeito nenhum no jogo.
		PlanetaNoEspaco? arlia = null;
		foreach (PlanetaNoEspaco q in Espaco.PreFeitos())
			if (q.Nome == "Arlia") { arlia = q; break; }

		if (arlia is { } disco)
		{
			ZoneKey voltaZona = pl.Zone;
			Vec2 voltaPos = pl.Pos;
			MoveToZone(pl.Id, ZonaDoEspaco, disco.Pos);
			pl.ChunkAtual = ChunkId.De(pl.Pos);
			TickDoEspaco(pl);
			Checa("...e depois do boot ele CONTINUA recusando pouso (o filtro le o registro relido)",
				  Espaco.EhEspaco(pl.Zone), $"{pl.Zone.Name}");
			MoveToZone(pl.Id, voltaZona, voltaPos);
		}
	}

	// =====================================================================
	// 10. OS GATES DA SKILL -- OS DOIS SENTIDOS EM CADA
	// =====================================================================
	/// <summary>
	/// ============================ UM GATE SO VISTO RECUSANDO NAO E UM GATE ============================
	/// O bloco 6 media as recusas do verbo. Ele nao media nenhum "sim" isolado -- e uma porta que so
	/// foi vista fechada e indistinguivel de uma parede. Este bloco fecha os DOIS sentidos de cada
	/// portao e, de quebra, cobre dois que ninguem media:
	///
	///   * **OS 10 MARCOS.** O bloco 6 chamava `PlanetDestroy(pl)` direto -- ou seja, pulava o
	///     `UsarTecnica`, que e onde mora o `SabeTecnica`. Com isso, o custo de 10 marcos da skill
	///     (`skills.json`: `custo: 10`, `custofixo: 1`) nao era medido em lugar nenhum: um servidor que
	///     desse Planet Destroy de graça a todo mundo passava na bancada inteira. Aqui o verbo entra
	///     pela porta de producao, com o id que o cliente manda.
	///   * **O BP CONTRA A GRAVIDADE.** Nao havia checagem nenhuma. E ele tem uma armadilha propria:
	///     gravidade maior sobe o limiar E derruba o `expressedBP` (`StatCurves.GravFelt`), entao
	///     "recusou num planeta pesado" sozinho nao prova nada. A medida aqui e feita em duas metades
	///     separadas -- a conta (`MortePlanetaria.BpExigido`, uma casa so, no Core) e a obediencia do
	///     verbo a ela -- e o par vive/morre com o MESMO BP e o MESMO Ki, mudando so a gravidade.
	/// ============================================================================================
	/// </summary>
	private void MedirOsGates(Checagem Checa, ServerPlayer pl)
	{
		const string Path = "/datum/skill/Ki_Control/Planet_Destroy";

		if (_skills == null) { Checa("o catalogo de skills carregou", false); return; }

		_mortos.Limpar();
		_cargaDoPlanetDestroy.Clear();

		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, Cobaia, PontoDeNascimento(Cobaia));

		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate?.Corpo.Curar(1e9);
		pl.Combate?.SincronizarVida();

		// ---- 1. VILAO, NA PORTA DO APRENDIZADO (que e onde o DM o poe) ----
		// `villainonly = 1 //only an admin-designated Villain can learn it` (`Planets.dm:382`): a
		// palavra e **learn**. O bloco 6 media o bit no verbo; aqui ele e medido onde ele nasce.
		pl.Livro.Esquecer(Path);
		pl.Livro.MarcosLivres = 99;
		pl.Ficha.isVillain = false;

		Aprender(pl, Path);
		Checa("**QUEM NAO E VILAO NAO APRENDE** Planet Destroy (o `villainonly` de Planets.dm:382)",
			  !pl.Livro.Sabe(Path));

		// ---- 2. OS 10 MARCOS, os dois sentidos ----
		int custo = SkillCatalog.CustoDe(_skills.Get(Path)!);

		pl.Ficha.isVillain = true;
		pl.Livro.MarcosLivres = custo - 1;
		Aprender(pl, Path);

		// O NUMERO E LIDO ANTES DA CHAMADA, e nao dentro da mensagem: com o gate quebrado o
		// `Aprender` GASTA os marcos, e a linha vermelha sairia dizendo "-1 marcos" -- descrevendo o
		// estrago em vez da montagem.
		Checa($"...e nem o vilao aprende com {custo - 1} marcos (a skill custa {custo})",
			  !pl.Livro.Sabe(Path));

		pl.Livro.MarcosLivres = custo;
		Aprender(pl, Path);
		Checa("**E COM OS MARCOS NA MAO ELE APRENDE** (o portao abre, e nao e uma parede)",
			  pl.Livro.Sabe(Path));
		Checa("...e os marcos foram COBRADOS (aprender nao e de graca)",
			  pl.Livro.MarcosLivres == 0, $"restaram {pl.Livro.MarcosLivres}");

		// ---- 3. O VERBO PELA PORTA DE PRODUCAO, e o gate do `SabeTecnica` ----
		// Daqui pra baixo tudo entra por `UsarTecnica(pl, "Planet_Destroy")` -- o mesmo id que o
		// cliente manda. E o que liga os 10 marcos acima ao botao de verdade.
		pl.Ficha.BP = 1e12;
		pl.Ficha.Tick(agoraMs: NowMs());
		pl.Ficha.Ki = 99_999;

		pl.Livro.Esquecer(Path);
		_cargaDoPlanetDestroy.Clear();
		UsarTecnica(pl, "Planet_Destroy");
		Checa("**SEM TER COMPRADO A SKILL O VERBO NAO SAI** -- e e por AQUI que os 10 marcos mordem",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id));

		pl.Livro.Dar(Path);
		pl.Ficha.Ki = 99_999;
		UsarTecnica(pl, "Planet_Destroy");
		Checa("...e com a skill na mao ele sai pelo mesmo caminho do cliente",
			  _cargaDoPlanetDestroy.ContainsKey(pl.Id));

		// ---- 4. A CONTA DA GRAVIDADE, isolada da ficha ----
		Checa("o exigido e `10000 x Planetgrav` (Planets.dm:326): 1x pede 10 mil, 10x pede 100 mil",
			  Math.Abs(MortePlanetaria.BpExigido(1) - 10_000) < 0.001
			  && Math.Abs(MortePlanetaria.BpExigido(10) - 100_000) < 0.001,
			  $"{MortePlanetaria.BpExigido(1):0} / {MortePlanetaria.BpExigido(10):0}");
		Checa("...e gravidade abaixo de 1 nao barateia (o piso -- no espaco `Planetgrav` e 0)",
			  Math.Abs(MortePlanetaria.BpExigido(0) - 10_000) < 0.001);

		// ---- 5. E O VERBO OBEDECE A ELA: mesmo BP, mesmo Ki, so a gravidade muda ----
		// ============================ A JANELA ENTRE OS DOIS LIMIARES ============================
		// O BP e escolhido pra cair ENTRE `BpExigido(leve)` e `BpExigido(pesado)`, **medido ja no chao
		// pesado**. Assim o unico fato que muda de um lado pro outro e o limiar: no chao pesado o
		// `expressedBP` esta abaixo do exigido, e no leve -- com a MESMA ficha -- ele esta acima, porque
		// alem de o limiar cair, a gravidade menor devolve poder. As duas coisas puxam pro mesmo lado, e
		// e por isso que a conta acima e afirmada em separado: aqui se mede obediencia, nao aritmetica.
		// ====================================================================================
		double gravLeve = 1, gravPesada = 10;
		double alvo = (MortePlanetaria.BpExigido(gravLeve) + MortePlanetaria.BpExigido(gravPesada)) / 2;

		pl.Ficha.Planetgrav = gravPesada;
		AjustarExpressoPara(pl, alvo);
		double bpNaJanela = pl.Ficha.BP;

		Checa("(montagem) com a ficha assim, o BP expresso cai ENTRE os dois limiares",
			  pl.Ficha.expressedBP > MortePlanetaria.BpExigido(gravLeve)
			  && pl.Ficha.expressedBP < MortePlanetaria.BpExigido(gravPesada),
			  $"expresso {pl.Ficha.expressedBP:0} entre {MortePlanetaria.BpExigido(gravLeve):0} "
			  + $"e {MortePlanetaria.BpExigido(gravPesada):0}");

		pl.Ficha.Ki = 99_999;
		_cargaDoPlanetDestroy.Clear();
		UsarTecnica(pl, "Planet_Destroy");
		Checa($"**NUM CHAO {gravPesada:0}x MAIS PESADO ESSE BP NAO BASTA** (o `10000 * Planetgrav`)",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id));

		pl.Ficha.BP = bpNaJanela;
		pl.Ficha.Planetgrav = gravLeve;
		pl.Ficha.Tick(agoraMs: NowMs());
		pl.Ficha.Ki = 99_999;
		UsarTecnica(pl, "Planet_Destroy");
		Checa("...e **O MESMO BP E O MESMO KI BASTAM** num chao de gravidade 1 -- so a gravidade mudou",
			  _cargaDoPlanetDestroy.ContainsKey(pl.Id),
			  $"BP {pl.Ficha.BP:0}, expresso {pl.Ficha.expressedBP:0}");

		// ---- 6. OS 30 S, OS DOIS SENTIDOS ----
		// O bloco 6 so via o "sim" no 30o segundo. Sem o "ainda nao" no 29o, um `SegundosDeCarga`
		// zerado -- ou um tique que acendesse a destruicao na hora -- passaria despercebido, e a janela
		// pra nocautear o vilao (que e a unica defesa que existe contra isto) nao existiria.
		_mortos.Limpar();
		_cargaDoPlanetDestroy.Clear();
		pl.Ficha.BP = 1e12;
		pl.Ficha.Tick(agoraMs: NowMs());
		pl.Ficha.Ki = 99_999;
		UsarTecnica(pl, "Planet_Destroy");

		for (int i = 0; i < (int)MortePlanetaria.SegundosDeCarga - 1; i++) TickDaDestruicao(1);
		Checa($"**NO {MortePlanetaria.SegundosDeCarga - 1:0}o SEGUNDO A CARGA AINDA NAO CONDENOU NADA** "
			+ "(o outro sentido dos 30 s: a janela e uma janela)",
			  _cargaDoPlanetDestroy.ContainsKey(pl.Id) && MorteDaZona(Cobaia) == null,
			  $"carga={_cargaDoPlanetDestroy.ContainsKey(pl.Id)}, planeta={MorteDaZona(Cobaia)?.Fase}");

		TickDaDestruicao(1);
		TickDaDestruicao(1);
		Checa($"...e no {MortePlanetaria.SegundosDeCarga:0}o ela acende (o portao abre, nao trava)",
			  MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo, $"{MorteDaZona(Cobaia)?.Fase}");

		AbortarMorte(Cobaia, "bancada-gates");
		MoveToZone(pl.Id, voltaZona, voltaPos);
	}


	// =====================================================================
	// 15. (K) DERRUBAR UM MUNDO COM KI, DO ESPACO
	// =====================================================================
	/// <summary>
	/// ============================ O QUE SO DAQUI SE RESPONDE ============================
	/// O pedido do dono, inteiro: *"pessoas q estao no espaco poderiam jogar ataques de KI no planeta
	/// pra comecar a causar dano nele ... ao zerar a vida do planeta, ia comecar a contagem dos 5
	/// minutos igual e com planet destroy"*. **Nao ha uma linha disso no DM** -- o `obj/Planets` de la
	/// tem tres booleanos e zero vida --, entao aqui nao ha original pra conferir contra: o que da pra
	/// afirmar e que as pecas usadas sao as que ja existiam e que o efeito chega ao MUNDO.
	///
	///   1. **A VIDA E A FURIA, MEDIDA PELO LIMIAR E NAO PELA FORMULA.** Um epsilon abaixo da furia o
	///      mundo continua vivo; chegando nela, ele cai. Isso afirma o *"dano de quando ele explode =
	///      vida do planeta"* pelo COMPORTAMENTO -- comparar duas contas ficaria verde se uma fosse
	///      copia da outra.
	///   2. **O TIRO DO ESPACO ACERTA O PLANETA?** Pelo tique de producao, com um projetil de verdade
	///      disparado pela porta de verdade, nascendo FORA do disco. Esta e a metade que uma checagem
	///      de formula nunca responde: antes desta fase um tiro no espaco **atravessava o disco sem
	///      nada acontecer**, e toda a aritmetica do dano teria ficado verde do mesmo jeito.
	///   3. **O AVISO NAO VAZA NUMERO.** Medido no FIO (`EscutaDeAvisos`), varrendo DIGITO por digito
	///      cada linha que este sistema produz. O dono foi explicito, e a tentacao de ajudar com um
	///      numero e enorme.
	///   4. **E NAO VAZA PELO LADO DE FORA.** Um mundo ferido **nao entra no registro de mortos** --
	///      se entrasse, `ZonaCondenada` passaria a dizer "sim" por causa de um tiro de raspao e
	///      desligaria povoamento, berco, invasao, dominio e pouso; e o `S2C.Mortos` (que so carrega o
	///      registro) levaria a ferida pro cliente de brinde.
	///   5. **O PORTAO TEM OS DOIS SENTIDOS**, e ele mora DENTRO da formula (nao so no chamador).
	///   6. **ZERAR A VIDA ABRE A MESMA PORTA**, e nao uma copia dela: o commit tem que trazer junto
	///      tudo o que so o `ComecarDestruicao` escreve (fase, estagio, prazo, tremor e ceu).
	///   7. **AS BORDAS**: dois atacantes somam; o algoz que sumiu nao impede nada; um mundo ja
	///      condenado nao reinicia nem acelera; um mundo DESTRUIDO deixa o tiro passar; a ferida fecha
	///      sozinha -- **mas nao debaixo de fogo**, que e o buraco que a carencia tapa.
	/// ================================================================================
	/// </summary>
	private void MedirODerrubarComKi(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();
		_feridasDeMundo.Clear();
		_faleiDoMundo.Clear();

		ZoneKey alvo = Cobaia;
		(_, double g) = MedidasDoPlaneta(alvo);
		double vida = FuriaDoPlaneta(alvo);
		double portao = MortePlanetaria.BpExigido(g);

		// =============================================================
		// (a) A MESA: o portao, a ancora e os dois eixos
		// =============================================================
		Checa("(montagem) a cobaia e um planeta de gravidade > 1 -- senao o portao dela seria o da Terra",
			  g > 1 && vida > 0, $"grav {g}, vida {vida:N0}");

		// O PORTAO, NOS DOIS SENTIDOS, e por DENTRO da formula: um dano enorme com BP abaixo do
		// limiar tem que sair ZERO. Se o crivo morasse so no chamador, o dia em que aparecesse um
		// segundo chamador seria o dia em que um fracote derruba um mundo.
		Checa("**K5: abaixo do limiar o tiro nao faz NADA** -- e o zero sai de dentro da formula",
			  MortePlanetaria.DanoNoMundo(1e9, g, portao * 0.999) == 0, $"limiar {portao:N0}");
		Checa("...e no limiar exato ele ja fere",
			  MortePlanetaria.DanoNoMundo(MortePlanetaria.BrutoDeReferencia, g, portao) > 0);
		Checa("...e o portao e o MESMO numero que o verb Planet Destroy cobra (uma regua, tres usos)",
			  MortePlanetaria.ForteOBastantePraFerirOMundo(portao, g)
			  && !MortePlanetaria.ForteOBastantePraFerirOMundo(portao - 1, g));

		// A ANCORA, afirmada como FRASE: no limiar, com o tiro mais cru do jogo, sao `TirosNoLimiar`.
		// Sem isto o `MundoPorPontoDeKi` seria um numero magico que ninguem consegue conferir.
		double tirosNoLimiar = MortePlanetaria.Furia(MortePlanetaria.LadoDeReferencia, 1)
			/ MortePlanetaria.DanoNoMundo(MortePlanetaria.BrutoDeReferencia, 1,
										  MortePlanetaria.BpExigido(1));
		Checa($"**A ANCORA VALE**: no limiar da Terra o tiro mais cru derruba o mundo em "
			  + $"{MortePlanetaria.TirosNoLimiar:N0} tiros",
			  Math.Abs(tirosNoLimiar - MortePlanetaria.TirosNoLimiar) < 1e-6, $"{tirosNoLimiar:N1}");

		// E O TIRO DE REFERENCIA E O DA CADEIA DE KI, e nao o literal 12: se alguem dobrar o
		// `DanoGlobalDeKi`, a ancora tem que acompanhar em vez de mentir caladamente.
		Checa("...e o 'tiro de referencia' e o que a CADEIA DE KI devolve pro tiro mais simples",
			  Math.Abs(MortePlanetaria.BrutoDeReferencia
					   - DanoDeKi.BrutoContra(mods: 1, baseDano: 1, maxDano: 0, defesa: 0)) < 1e-9,
			  $"{MortePlanetaria.BrutoDeReferencia:0.##}");

		// OS DOIS EIXOS SAO INDEPENDENTES -- pericia e poder. Se um deles nao movesse o dano, metade
		// do pedido do dono estaria morta e a outra metade continuaria verde.
		Checa("mais PODER, mais estrago (com a mesma pericia)",
			  MortePlanetaria.DanoNoMundo(12, g, portao * 100)
			  > MortePlanetaria.DanoNoMundo(12, g, portao * 10));
		Checa("mais PERICIA, mais estrago (com o mesmo poder)",
			  MortePlanetaria.DanoNoMundo(1200, g, portao) > MortePlanetaria.DanoNoMundo(12, g, portao));

		// =============================================================
		// (b) K4: A TABELA -- o pedido literal do briefing
		// =============================================================
		// A superficie inteira, e nao uma linha: as duas metades do pedido do dono ("MUITO fortes
		// zeram rapidamente" e "mais fracas demoram mt mais") vivem no eixo do BP, mas quem decide se
		// um numero e absurdo ou nao e a PERICIA, que na pratica anda junto com ele.
		GD.Print("  --   K4: QUANTOS TIROS PRA DERRUBAR CADA MUNDO (Basic Blast, ~3,3 tiros/s)");
		GD.Print("       pericia: CRU (nunca treinou, bruto 12) | INICIANTE 200 | VETERANO 3.000 | MESTRE 27.000");
		foreach (string nome in new[] { "Earth", "Vegeta", "Icer" })
		{
			var z = ZoneKey.Premade(nome);
			(_, double gp) = MedidasDoPlaneta(z);
			double vp = FuriaDoPlaneta(z);
			GD.Print($"       {nome} -- vida {vp:N0}, limiar de BP expresso {MortePlanetaria.BpExigido(gp):N0}");
			foreach (double bp in new double[] { 1e4, 1e5, 1e6, 1e7, 1e9 })
			{
				string linha = $"         BP {bp,-9:0.0e0}";
				foreach (double bruto in new double[] { 12, 200, 3000, 27000 })
				{
					double d = MortePlanetaria.DanoNoMundo(bruto, gp, bp);
					linha += d <= 0 ? "     -- nao fere --"
									: $"   {Math.Max(Math.Ceiling(vp / d), 1),10:N0} tiros";
				}
				GD.Print(linha);
			}
		}
		int ladoG80 = MundoProcedural.LadoDaGravidade(80);
		double vidaG80 = MortePlanetaria.Furia(ladoG80, 80);
		GD.Print($"       gerado g80 (o mundo mais duro que existe) -- vida {vidaG80:N0}, "
			   + $"limiar {MortePlanetaria.BpExigido(80):N0}: com BP 1e9 e pericia de mestre, "
			   + $"{Math.Max(Math.Ceiling(vidaG80 / MortePlanetaria.DanoNoMundo(27000, 80, 1e9)), 1):N0} tiros");

		// AS DUAS PONTAS DA TABELA, afirmadas: a lenta e lenta, a rapida e rapida.
		double tirosFraco = vida / MortePlanetaria.DanoNoMundo(MortePlanetaria.BrutoDeReferencia, g, portao);
		double tirosForte = vida / MortePlanetaria.DanoNoMundo(27000, g, portao * 1000);
		Checa("**K4 (fraco): quem esta NO limiar leva milhares de tiros** -- a rota de orbita e a lenta, "
			  + "e tem que ser mais cara que os 30 s do verb",
			  tirosFraco >= 1000, $"{tirosFraco:N0} tiros");
		Checa("**K4 (forte): mil vezes o limiar derruba o mesmo mundo num tiro so**",
			  tirosForte <= 1, $"{tirosForte:0.000} tiros");

		// =============================================================
		// (c) K1: O TIRO DO ESPACO ACERTA O PLANETA -- pelo tique de PRODUCAO
		// =============================================================
		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		double bpGuardado = pl.Ficha.BP;

		PlanetaNoEspaco? achado = null;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (p.Nome == alvo.Name) { achado = p; break; }

		if (achado is not { } disco)
		{
			Checa("(montagem) a cobaia esta na carta estelar", false, $"nao achei o corpo de {alvo.Name}");
			return;
		}

		try
		{
			// ============================ ELE NASCE FORA DO DISCO ============================
			// Esta e a armadilha nomeada deste projeto: uma bancada que ja comecasse com o tiro DENTRO
			// do planeta nunca testaria a ENTRADA nele. `PontoDeDecolagem` e o mesmo lugar em que o
			// jogo larga quem decola -- 90 px acima da superficie --, e o tiro percorre esses 90 px
			// sozinho, sub-passo por sub-passo, no tique de producao.
			// ============================================================================
			MoveToZone(pl.Id, ZonaDoEspaco, Espaco.PontoDeDecolagem(disco));
			pl.ChunkAtual = ChunkId.De(pl.Pos);
			pl.Ficha.dead = false;
			pl.Ficha.KO = false;

			Checa("(montagem) o atirador comeca FORA do disco do planeta",
				  (pl.Pos - disco.Pos).LengthSquared > disco.Raio * disco.Raio,
				  $"{(pl.Pos - disco.Pos).Length:0} px do centro, raio {disco.Raio:0}");

			AjustarExpressoPara(pl, portao * 50);

			// PRA BAIXO: o ponto de decolagem fica ACIMA do planeta (`Pos + (0, -(raio+90))`).
			Projetil tiro = Disparar(pl, TiroDeBancada(), rumoDado: new Vec2(0, 1), verbo: "bancada");
			double antes = FeridaDoMundo(alvo);
			for (int i = 0; i < 240 && tiro.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);

			// ============================ O TIRO PRECISA SER VISTO VOANDO -- e ele nao era ============================
			// Medido no FIO, com o escritor de PRODUCAO (`EscreverProjeteis`) e lendo o contador que
			// ele poe no pacote. O snapshot do espaco escrevia `hash 0` -- que nao e a zona de ninguem
			// -- com a nota *"os tiros ficam pro dia em que houver combate espacial"*, e esse dia e
			// este. Sem o conserto, TODAS as outras checagens desta familia continuavam verdes e o
			// jogador via a bola aparecer colada na mao, parar la, e o estouro surgir do nada em cima
			// do planeta. E a armadilha nomeada do projeto, na forma mais pura: o servidor certo, o
			// mundo certo, e nada na tela.
			//
			// Um tiro NOVO pra medir o voo, porque o de cima ja morreu no planeta.
			// ==================================================================================================
			Projetil emVoo = Disparar(pl, TiroDeBancada(), rumoDado: new Vec2(0, 1), verbo: "bancada");
			var fio = new LiteNetLib.Utils.NetDataWriter();
			EscreverProjeteis(fio, ZonaDoEspaco.Hash, pl.Pos);
			int noFio = new LiteNetLib.Utils.NetDataReader(fio.Data, 0, fio.Length).GetUShort();
			Matar(emVoo, FimDeProjetil.Cessou);

			Checa("**o tiro do espaco VIAJA NO SNAPSHOT** -- senao ele apareceria colado na mao e o "
				  + "estouro surgiria do nada em cima do planeta",
				  noFio > 0, $"{noFio} tiros no bloco do snapshot do espaco");

			// O MOTIVO DA MORTE E O QUE ESCOLHE O DESENHO no cliente (`World.AoMorrerTiro`), e ele
			// tem que ser `Mundo` e nao `Cenario`: a regra dos dois e a mesma, a ESCALA nao. Com
			// `Cenario`, o estouro de um muro de 32 px aconteceria em cima de um disco de 440 -- o
			// servidor certo, o mundo certo, e nada visivel na tela.
			Checa("**K1: O TIRO DO ESPACO ENCOSTA NO PLANETA** (e acaba nele, com o motivo que o "
				  + "cliente usa pra desenhar um estouro do tamanho de um mundo)",
				  !tiro.Vivo && tiro.Fim == FimDeProjetil.Mundo, $"fim = {tiro.Fim}");
			Checa("**...e o MUNDO sentiu** -- a ferida existe depois do tique de producao",
				  FeridaDoMundo(alvo) > antes,
				  $"{antes:0.##} -> {FeridaDoMundo(alvo):0.##} (vida {vida:N0})");

			// A OUTRA METADE: um tiro que nao encosta em nada nao fere ninguem. Sem ela, um
			// `MundoNoCaminho` que devolvesse planeta pra qualquer ponto do universo ficaria verde.
			_feridasDeMundo.Clear();
			Projetil praLonge = Disparar(pl, TiroDeBancada(), rumoDado: new Vec2(0, -1), verbo: "bancada");
			for (int i = 0; i < 400 && praLonge.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
			Checa("...e um tiro dado PRA LONGE do planeta nao fere mundo nenhum",
				  FeridaDoMundo(alvo) == 0 && praLonge.Fim != FimDeProjetil.Mundo,
				  $"ferida {FeridaDoMundo(alvo):0.##}, fim = {praLonge.Fim}");

			// =============================================================
			// (d) K5: O AVISO NAO VAZA NUMERO -- medido no FIO
			// =============================================================
			_feridasDeMundo.Clear();
			_faleiDoMundo.Clear();
			List<string>? escutaAntes = EscutaDeAvisos;
			var ditas = new List<string>();
			EscutaDeAvisos = ditas;

			try
			{
				AjustarExpressoPara(pl, portao * 0.5);
				Projetil fraco = Disparar(pl, TiroDeBancada(), rumoDado: new Vec2(0, 1), verbo: "bancada");
				for (int i = 0; i < 240 && fraco.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);

				Checa("**K5: quem nao e forte o bastante e AVISADO** -- e o tiro nao arranha o mundo",
					  ditas.Any(t => t.Contains("forte")) && FeridaDoMundo(alvo) == 0,
					  $"{ditas.Count} linhas, ferida {FeridaDoMundo(alvo):0.##}");

				// ============================ A CHECAGEM QUE DA NOME A ESTA FAMILIA ============================
				// Digito por digito. O dono escreveu *"nao e pra dizer o bp minimo ou outra coisa"*, e
				// "outra coisa" e a parte dificil: o limiar, o BP do atirador, quanto falta, a razao
				// entre os dois, a vida restante. Varrer por DIGITO pega todos de uma vez -- inclusive
				// o que alguem acrescentar daqui a seis meses sem ler este comentario.
				// ==========================================================================================
				var comNumero = ditas.Where(t => t.Any(char.IsDigit)).ToList();
				Checa("**K5: NENHUMA linha deste sistema carrega um numero** (nem o limiar, nem o que "
					  + "falta, nem a vida do mundo)",
					  comNumero.Count == 0,
					  comNumero.Count == 0 ? "" : $"vazou: {string.Join(" | ", comNumero)}");

				// E O MUNDO FERIDO NAO ENTRA NO REGISTRO DE MORTOS -- o vazamento "pelo lado de fora".
				// Se entrasse, o cliente receberia a entrada no `S2C.Mortos` E o jogo inteiro passaria
				// a tratar um arranhao como condenacao (povoamento, berco, invasao, dominio, pouso).
				ditas.Clear();
				_faleiDoMundo.Clear();
				AjustarExpressoPara(pl, portao * 50);
				Projetil forte = Disparar(pl, TiroDeBancada(), rumoDado: new Vec2(0, 1), verbo: "bancada");
				for (int i = 0; i < 240 && forte.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);

				Checa("**um mundo FERIDO nao esta condenado** -- a ferida mora fora do registro de "
					  + "mortos, e por isso nada dela viaja no `S2C.Mortos`",
					  FeridaDoMundo(alvo) > 0 && !ZonaCondenada(alvo) && _mortos.Quantos == 0,
					  $"ferida {FeridaDoMundo(alvo):0.##}, condenado {ZonaCondenada(alvo)}, "
					  + $"mortos {_mortos.Quantos}");
				Checa("...e o retorno ao ATIRADOR tambem nao tem numero",
					  !ditas.Any(t => t.Any(char.IsDigit)),
					  string.Join(" | ", ditas.Where(t => t.Any(char.IsDigit))));

				// =============================================================
				// (e) K2: O CHAO E AVISADO -- e nao fica sabendo QUEM
				// =============================================================
				_feridasDeMundo.Clear();
				_faleiDoMundo.Clear();
				MoveToZone(pl.Id, alvo, PontoDeNascimento(alvo));

				// O atirador e um corpo forjado com nome distinto: e assim que "o aviso nao nomeia o
				// atacante" vira uma checagem em vez de uma frase de comentario.
				ServerPlayer orbita = ForjarEm(alvo, "SombraDaOrbita", 1);
				Projetil daOrbita = TiroForjado(portao * 50);

				// ============================ A JANELA E EXATAMENTE O ATO MEDIDO ============================
				// O `ditas.Clear()` estava tres linhas acima, e as duas rodadas anteriores desta bancada
				// reprovaram por isso -- por motivos DIFERENTES, e os dois de montagem: pousar em Arlia
				// dispara o aviso de gravidade (*"o chao de Arlia puxa 2 vezes mais forte"*), e forjar um
				// corpo NA MESMA ZONA dispara o mesmo aviso pra ele (a `EscutaDeAvisos` e um funil unico:
				// ela pega toda linha do servidor, e nao so as do jogador que interessa).
				//
				// A varredura por digito e severa de proposito, entao a janela tem que abrir DEPOIS de
				// toda a montagem -- senao ela mede o vizinho e a bancada acusa um vazamento que nao
				// existe. Uma checagem que reprova por artefato proprio e tao ruim quanto uma que passa.
				// ======================================================================================
				ditas.Clear();

				for (int i = 0; i < 5; i++) AtingirMundoComKi(disco, daOrbita, orbita);

				var doChao = ditas.Where(t => t.Contains("espaço")).ToList();
				Checa("**K2: quem esta no planeta e avisado do ataque vindo do espaco**",
					  doChao.Count > 0, $"{ditas.Count} linhas: {string.Join(" | ", ditas)}");
				Checa("...e o aviso e UM so, e nao um por tiro (cinco tiros, uma linha)",
					  doChao.Count == 1, $"{doChao.Count} avisos pra 5 tiros");
				Checa("**...e ele NAO diz quem esta atacando** -- do chao ninguem enxerga a orbita",
					  !ditas.Any(t => t.Contains("SombraDaOrbita")), string.Join(" | ", ditas));
				Checa("...e tambem nao carrega numero",
					  !ditas.Any(t => t.Any(char.IsDigit)),
					  string.Join(" | ", ditas.Where(t => t.Any(char.IsDigit))));

				// O ALGOZ QUE SUMIU: o mesmo tiro sem atirador nenhum. E a borda "o atacante morre no
				// meio" -- a ferida e do PLANETA, e nao de quem atira.
				double antesDoOrfao = FeridaDoMundo(alvo);
				AtingirMundoComKi(disco, daOrbita, atirador: null);
				Checa("**borda: o atacante sumiu entre o disparo e o impacto** -- o mundo sente igual",
					  FeridaDoMundo(alvo) > antesDoOrfao,
					  $"{antesDoOrfao:0.##} -> {FeridaDoMundo(alvo):0.##}");

				// ============================ E DO CHAO NAO SE FERE O PROPRIO PLANETA ============================
				// A regra do K mora num `if (noEspaco ...)` dentro do laco de sub-passos, e "ligada num
				// chamador e esquecida no outro" e uma familia de defeito que este projeto ja pagou --
				// aqui ela apareceria ao contrario: um tiro dado DENTRO do mapa ferindo o mundo em que
				// quem atira esta pisando. `pl` ja esta em Arlia; um tiro de verdade, e o mundo intacto.
				// ==========================================================================================
				_feridasDeMundo.Clear();
				Projetil doChaoMesmo = Disparar(pl, TiroDeBancada(), verbo: "bancada");
				for (int i = 0; i < 240 && doChaoMesmo.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
				Checa("**um tiro dado DE DENTRO do planeta nao fere o planeta** (a regra e do espaco, "
					  + "e nao do mapa)",
					  FeridaDoMundo(alvo) == 0, $"ferida {FeridaDoMundo(alvo):0.##}, fim {doChaoMesmo.Fim}");

				Recolher(orbita);
			}
			finally { EscutaDeAvisos = escutaAntes; }

			// =============================================================
			// (f) K3 + K6: a vida E a furia, e zera-la abre a MESMA porta
			// =============================================================
			_feridasDeMundo.Clear();
			_mortos.Limpar();

			// UM EPSILON ABAIXO: vivo.
			FerirOMundo(alvo, alvo.Name, vida * 0.999, portao, null, "bancada-limiar");
			Checa("**K3: um epsilon ABAIXO da furia, o mundo ainda esta vivo**",
				  !ZonaCondenada(alvo) && FeridaDoMundo(alvo) > 0,
				  $"ferida {FeridaDoMundo(alvo):N0} de {vida:N0}");

			// ...E CHEGANDO NELA: a mesma porta do Planet Destroy, com tudo o que so ela escreve.
			bool caiu = FerirOMundo(alvo, alvo.Name, vida * 0.002, portao, null, "bancada-limiar");
			EstadoDaMorte? e = MorteDaZona(alvo);

			Checa("**K3: chegando na furia o mundo cai** -- a vida E o dano da explosao, um numero so",
				  caiu && e != null, $"caiu={caiu}");
			Checa("**K6: e ele cai pela MESMA PORTA do Planet Destroy**, com tudo o que so o "
				  + "`ComecarDestruicao` escreve (fase, estagio, prazo, relogio de tremor e ceu)",
				  e is { Fase: FaseDaMorte.Explodindo }
				  && e.Estagio == MortePlanetaria.UltimoEstagio + 1
				  && Math.Abs(e.Faltam - MortePlanetaria.SegundosDeExplosao) < 1e-9
				  && _proximoTremor.ContainsKey(e.Chave)
				  && _climaForcado.TryGetValue(alvo.Hash, out ClimaForcado ceu)
				  && ceu.Tipo == TipoDeClima.Destruicao,
				  e == null ? "sem registro" : $"fase {e.Fase}, estagio {e.Estagio}, faltam {e.Faltam:0}");
			Checa("...e a FERIDA deixa de existir (nao ha duas nocoes de 'este mundo esta acabando')",
				  FeridaDoMundo(alvo) == 0);

			// =============================================================
			// (g) AS BORDAS
			// =============================================================
			// UM MUNDO JA CONDENADO NAO REINICIA NEM ACELERA. Este e o caso que aparece de verdade:
			// um cerco em grupo, e alguem que continua atirando depois de o planeta ja ter caido.
			double faltavam = MorteDaZona(alvo)!.Faltam;
			_faleiDoMundo.Clear();
			AtingirMundoComKi(disco, TiroForjado(portao * 1000), null);
			Checa("**borda: atirar num mundo JA CONDENADO nao reinicia nem acelera a agonia**",
				  Math.Abs(MorteDaZona(alvo)!.Faltam - faltavam) < 1e-9 && FeridaDoMundo(alvo) == 0,
				  $"faltavam {faltavam:0.##}, faltam {MorteDaZona(alvo)!.Faltam:0.##}");

			// UM MUNDO DESTRUIDO NAO ESTA MAIS LA. O tiro atravessa o lugar onde ele ficava -- a mesma
			// coisa que o cliente ja faz ao parar de desenhar o disco.
			MorteDaZona(alvo)!.Fase = FaseDaMorte.Destruido;
			Checa("**borda: num mundo DESTRUIDO o tiro passa reto** (o planeta some pros dois lados)",
				  MundoNoCaminho(disco.Pos) == null);

			_mortos.Limpar();
			Checa("(montagem) e com ele vivo o MESMO ponto volta a ser solido",
				  MundoNoCaminho(disco.Pos) != null);

			// DOIS ATACANTES SOMAM NA MESMA FERIDA -- e o desenho certo pra um cerco.
			_feridasDeMundo.Clear();
			FerirOMundo(alvo, alvo.Name, vida * 0.3, portao, null, "atacante A");
			double depoisDeUm = FeridaDoMundo(alvo);
			FerirOMundo(alvo, alvo.Name, vida * 0.3, portao, null, "atacante B");
			Checa("**borda: dois atacantes somam na MESMA ferida** (a ferida e do planeta)",
				  Math.Abs(FeridaDoMundo(alvo) - depoisDeUm * 2) < 1e-6,
				  $"{depoisDeUm:N0} -> {FeridaDoMundo(alvo):N0}");

			// A CICATRIZACAO, E A CARENCIA -- as duas metades, e a segunda e a que tapa o buraco.
			double antesDoTempo = FeridaDoMundo(alvo);
			for (int s = 0; s < (int)MortePlanetaria.SegundosDeCalmaAntesDeFechar / 2; s++)
				TickDaDestruicao(1);
			Checa("**borda: DEBAIXO DE FOGO a ferida nao fecha** -- sem a carencia o portao seria "
				  + "mentira, porque quem ele aceita nao venceria a cicatrizacao",
				  Math.Abs(FeridaDoMundo(alvo) - antesDoTempo) < 1e-9,
				  $"{antesDoTempo:N0} -> {FeridaDoMundo(alvo):N0} dentro da carencia");

			for (int s = 0; s < 120; s++) TickDaDestruicao(1);
			Checa("...e passada a carencia ela COMECA a fechar",
				  FeridaDoMundo(alvo) < antesDoTempo,
				  $"{antesDoTempo:N0} -> {FeridaDoMundo(alvo):N0}");

			for (int s = 0; s < (int)MortePlanetaria.SegundosParaCicatrizar + 120; s++)
				TickDaDestruicao(1);
			Checa("...e o mundo fecha por INTEIRO, e a entrada some (num universo infinito, um "
				  + "arranhao guardado pra sempre seria dado orfao eterno)",
				  FeridaDoMundo(alvo) == 0 && _feridasDeMundo.Count == 0,
				  $"{_feridasDeMundo.Count} feridas vivas");

			// O PALCO DEVOLVE A FERIDA. Ela nao vai pro disco -- entao a pasta do dono nunca correu
			// risco por ela --, mas o MUNDO EM MEMORIA corria: uma bancada que abrisse a Terra a 90% e
			// fosse embora deixaria o servidor com um planeta a dois tiros de morrer, e o proximo
			// jogador que passasse por perto acabaria com ele sem entender.
			using (PalcoDeMortesDeBancada())
				FerirOMundo(ZoneKey.Premade("Earth"), "Earth", 1e6, 1e9, null, "dentro do palco");

			Checa("**o palco de bancada devolve a FERIDA junto com o registro**",
				  FeridaDoMundo(ZoneKey.Premade("Earth")) == 0 && _feridasDeMundo.Count == 0,
				  $"{_feridasDeMundo.Count} feridas vivas");
		}
		finally
		{
			_feridasDeMundo.Clear();
			_faleiDoMundo.Clear();
			_mortos.Limpar();
			_proximoTremor.Clear();
			ForcarClima(alvo, TipoDeClima.Limpo, 0);
			LimparProjeteisDeUmDono(pl.Id, ZonaDoEspaco.Hash);
			pl.Ficha.BP = bpGuardado;
			pl.Ficha.Tick(agoraMs: NowMs());
			MoveToZone(pl.Id, voltaZona, voltaPos);
		}
	}

	/// <summary>
	/// O TIRO DA BANCADA -- uma bola comum, pela receita de PRODUCAO.
	///
	/// Alcance de 30 tiles e o do Basic Blast: os 90 px ate a superficie cabem com folga, e a outra
	/// metade do teste (o tiro dado pro lado oposto) precisa que ele MORRA de alcance em vez de
	/// viajar pra sempre pelo universo.
	/// </summary>
	private static ReceitaDeProjetil TiroDeBancada() => new()
	{
		Tipo = TipoDeProjetil.Blast,
		BaseDano = 1,
		Velocidade = 1,
		AlcanceTiles = 30,
		Nome = "tiro de bancada",
	};

	/// <summary>
	/// UM TIRO PRONTO, sem passar pelo `Disparar` -- pras checagens que medem o MUNDO e nao o voo.
	///
	/// O voo ja foi provado na familia (c), com projetil de verdade no tique de producao; repeti-lo
	/// em cada borda so poria montagem entre a pergunta e a resposta.
	/// </summary>
	private static Projetil TiroForjado(double bp) => new()
	{
		Tipo = TipoDeProjetil.Blast,
		Bp = bp,
		ModsBase = 1,
		BaseDano = 1,
		Nome = "tiro de bancada",
	};

	/// <summary>
	/// PROCURA O BP QUE PRODUZ ESTE `expressedBP`, por bisseccao geometrica na cadeia de PRODUCAO.
	///
	/// Por que bisseccao e nao uma formula: `PowerLevel()` e uma cadeia de dezenas de fatores
	/// (gravidade, idade, Ki, HP, forma, raiva...) e inverte-la aqui seria escrever uma SEGUNDA casa da
	/// mesma conta -- exatamente o que a regra 0.4 proibe. A bisseccao pergunta a casa que existe.
	///
	/// `Tick` e barato e puro pra este fim (`Statify` + `PowerLevel` + `WeightTick`): ele nao treina,
	/// nao envelhece e nao regenera. Chamar 90 vezes nao move o personagem um milimetro.
	/// </summary>
	private void AjustarExpressoPara(ServerPlayer pl, double alvo)
	{
		double baixo = 1, alto = 1e18;
		for (int i = 0; i < 90; i++)
		{
			double meio = Math.Sqrt(baixo * alto);
			pl.Ficha.BP = meio;
			pl.Ficha.Tick(agoraMs: NowMs());
			if (pl.Ficha.expressedBP > alvo) alto = meio; else baixo = meio;
		}
		pl.Ficha.BP = baixo;
		pl.Ficha.Tick(agoraMs: NowMs());
	}
}
