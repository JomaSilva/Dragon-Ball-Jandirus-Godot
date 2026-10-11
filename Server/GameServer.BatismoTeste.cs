using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO BATISMO DE PLANETAS (`--batismoteste`).
///
/// O dono (2026-10-10): *"adicione tb a opçao de nomear o planeta (a primeira pessoa q entrou nele ou o dono
/// do planeta q dominou)"*.
///
/// No PRIMEIRO LOGIN, e hospedando: metade do que ela mede depende de um corpo com `Peer` (um NPC nao
/// descobre mundo nenhum), e os tres personagens de bancada pegam EMPRESTADO o do host -- devolvido no
/// `finally`, como a bancada do alem ja faz. O host entra como ADMIN de verdade, e e ele o admin das provas.
///
///   1. AS REGRAS PURAS: quem tem direito (a tabela inteira) e que nome serve (cada recusa).
///   2. A DESCOBERTA: o primeiro JOGADOR num mundo GERADO, e a porta `MoveToZone` de producao. (A OUTRA porta,
///      a do login, nao da pra medir daqui: esta bancada roda DENTRO do login, antes da linha que manda os
///      nomes. Quem a mede e a `--diagadmin`, no segundo passo -- o direito do admin ja esta no cliente antes
///      de qualquer verbo sair.)
///   3. O BATISMO PELO FUNIL DOS VERBOS: o descobridor uma vez, o dono com intervalo, o admin sempre; quem
///      nao tem direito ouve o motivo; e o nome invalido nao entra.
///   4. O DOMINIO MUDA O DIREITO: as duas pontas do livro da conquista (`FincarDominio`, `PerderDominio`)
///      mandam o direito novo.
///   5. O NOME NA BOCA DO SERVIDOR, o disco e a vassoura do wipe.
///
/// O defeito injetado (`Batismo.QualquerUmBatizaDeTeste`) e a regra de quem pode deixando de existir.
///
///     Godot --headless --path . --host --rede 7982 --batismoteste --raca Human --conta bancada_batismo --nome Padrinho
/// </summary>
public partial class GameServer
{
	private bool _batismoDeTeste;

	/// <summary>O que o <see cref="MandarBatismos"/> mandou: pra quem, com que direito, e quantos nomes. So com a bancada.</summary>
	internal static List<(int Quem, Batismo.Direito Direito, int Nomes)>? EscutaDeBatismos;

	/// <summary>
	/// PALCO DE BANCADA: enquanto esta ligado o livro dos nomes so muda na MEMORIA -- o `planetas-nomes.json`
	/// de quem roda a bancada nao e escrito. Lido pelo <see cref="SalvarBatismos"/>.
	/// </summary>
	internal bool BatismosSoNaMemoriaDeTeste;

	private void RodarBancadaDoBatismo(ServerPlayer pl)
	{
		GD.Print("\n[batismo] ================ O BATISMO DE PLANETAS (dono, 2026-10-10) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[batismo]   OK    {nome}"); }
			else { falhou++; GD.PrintErr($"[batismo]   FALHA {nome}   [{detalhe}]"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		List<NomeDePlaneta> nomesDeAntes = [.. _batismos.Todos];
		List<Dominio> dominiosDeAntes = [.. _dominios];
		bool arquivoExistia = System.IO.File.Exists(CaminhoDosBatismos);
		byte[]? arquivoDeAntes = arquivoExistia ? System.IO.File.ReadAllBytes(CaminhoDosBatismos) : null;
		ZoneKey zonaDeAntes = pl.Zone;
		Vec2 posDeAntes = pl.Pos;
		List<ServerPlayer> forjados = [];

		ZoneKey terra = ZoneKey.Premade("Earth");
		ZoneKey z1 = ZoneKey.Procedural("Mundo de Bancada", 0x6199_E7UL);
		ZoneKey z2 = ZoneKey.Procedural("Mundo de Bancada", 0x6199_E8UL);   // o MESMO nome, outra seed
		ZoneKey z3 = ZoneKey.Procedural("Terceiro Mundo", 0x6199_E9UL);
		static string ChaveDe(ZoneKey z) => ChaveDePlaneta.Da(z)!.Value.Texto;

		ServerPlayer Gente(string nome, string conta)
		{
			ServerPlayer f = Forjar(nome, pl.Pos, 1000);
			f.Conta = conta;
			f.Peer = pl.Peer;   // EMPRESTADO: sem `Peer` ele nao e jogador. Devolvido no `finally`.
			forjados.Add(f);
			return f;
		}
		Dominio Dominar(ServerPlayer dono, ZoneKey zona)
		{
			var d = new Dominio
			{
				PreFeito = false, Planeta = zona.Name, Seed = zona.Seed,
				Assinatura = dono.Assinatura, Conta = dono.Conta, Nome = dono.Name,
				Desde = TempoDoMundo, Coletado = TempoDoMundo, Visto = TempoDoMundo,
			};
			_dominios.Add(d);
			return d;
		}
		List<string> Pedir(ServerPlayer quem, string cmd, string arg = "") => Ouvir(() => Verbo(quem, cmd, arg));

		try
		{
			_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
			_batismos.Limpar();

			// =====================================================================
			// 1) AS REGRAS PURAS
			// =====================================================================
			GD.Print("[batismo] -- 1) as regras puras --");
			Checa("QUEM PODE: admin sempre; dono sempre; descobridor so com o direito inteiro e SEM dono no planeta; o resto ninguem",
				  Batismo.DireitoDe(admin: true, false, false, false, false) == Batismo.Direito.Admin
				  && Batismo.DireitoDe(false, donoAqui: true, temDono: true, false, false) == Batismo.Direito.Dono
				  && Batismo.DireitoDe(false, false, temDono: false, descobridor: true, direitoUsado: false) == Batismo.Direito.Descobridor
				  && Batismo.DireitoDe(false, false, temDono: false, descobridor: true, direitoUsado: true) == Batismo.Direito.Nenhum
				  && Batismo.DireitoDe(false, false, temDono: true, descobridor: true, direitoUsado: false) == Batismo.Direito.Nenhum
				  && Batismo.DireitoDe(false, false, false, false, false) == Batismo.Direito.Nenhum);
			Checa("o texto digitado e ARRUMADO (pontas e espacos repetidos), e nao cortado",
				  Batismo.Limpar("   Nova   Vegeta  ") == "Nova Vegeta" && Batismo.Limpar(null) == "");

			string[] conhecidos = [.. NomesConhecidos()];
			Batismo.Recusa V(string nome, string atual = "Mundo de Bancada", bool emUso = false) =>
				Batismo.Validar(nome, atual, conhecidos, _ => emUso);
			Checa("QUE NOME SERVE: 3 a 24 caracteres (os limites do `pspace_offer_name`)",
				  V("ab") == Batismo.Recusa.Curto && V("abc") == Batismo.Recusa.Pode
				  && V(new string('a', 24)) == Batismo.Recusa.Pode && V(new string('a', 25)) == Batismo.Recusa.Longo);
			Checa("...so letras (com acento), numeros, espaco, hifen e apostrofo",
				  V("Nova Vegeta-2") == Batismo.Recusa.Pode && V("Açaí d'Oeste") == Batismo.Recusa.Pode
				  && V("Nome<b>") == Batismo.Recusa.Caractere && V("a/b/c") == Batismo.Recusa.Caractere);
			Checa("...nome de MUNDO CONHECIDO e recusado, sem ligar pra caixa: os planetas do mapa, a \"Terra\" e os lugares do alem",
				  V("namek") == Batismo.Recusa.Conhecido && V("TERRA") == Batismo.Recusa.Conhecido
				  && V("Earth") == Batismo.Recusa.Conhecido && V("Makyo Star") == Batismo.Recusa.Conhecido
				  && V("Hell") == Batismo.Recusa.Conhecido,
				  $"namek={V("namek")} terra={V("TERRA")} earth={V("Earth")} makyo={V("Makyo Star")} hell={V("Hell")}");
			Checa("...e o nome que OUTRO planeta ja leva, ou o que este ja tem, tambem",
				  V("Bancadia", emUso: true) == Batismo.Recusa.EmUso && V("Bancadia", atual: "Bancadia") == Batismo.Recusa.Igual);

			// =====================================================================
			// 2) A DESCOBERTA
			// =====================================================================
			GD.Print("[batismo] -- 2) a descoberta: o primeiro jogador num mundo gerado --");
			ServerPlayer a = Gente("Ana", "bancada_batismo_a");
			ServerPlayer b = Gente("Beto", "bancada_batismo_b");
			ServerPlayer c = Gente("Caio", "bancada_batismo_c");
			Checa("(montagem) tres personagens de bancada com assinaturas DIFERENTES, e nenhum e admin",
				  new[] { a.Assinatura, b.Assinatura, c.Assinatura, pl.Assinatura }.Distinct().Count() == 4
				  && !EhAdmin(a) && !EhAdmin(b) && !EhAdmin(c) && EhAdmin(pl) && EhJogador(a));

			a.Zone = z1;
			List<string> falas = Ouvir(() => AoPisarNoMundo(a));
			Checa("**O PRIMEIRO A PISAR num mundo gerado vira o descobridor dele** -- e fica sabendo que pode dar o nome",
				  _batismos.De(ChaveDe(z1)) is { Descobridor: "Ana", DireitoUsado: false, Nome: "" } && Disse(falas, "primeiro a pisar"),
				  $"descobridor='{_batismos.De(ChaveDe(z1))?.Descobridor}' | {Ultimos(falas)}");

			b.Zone = z1;
			falas = Ouvir(() => AoPisarNoMundo(b));
			Checa("...e o SEGUNDO nao: o descobridor continua sendo o primeiro, e o segundo nao ouve nada",
				  _batismos.De(ChaveDe(z1))?.Descobridor == "Ana" && !Disse(falas, "primeiro a pisar"), Ultimos(falas));

			// UM CORPO SEM DONO chega antes num outro mundo: ele nao descobre nada, e o jogador que vem depois descobre.
			ServerPlayer npc = Forjar("Bicho", pl.Pos, 1000);
			forjados.Add(npc);
			npc.Zone = z2;
			AoPisarNoMundo(npc);
			bool npcNaoDescobriu = _batismos.De(ChaveDe(z2)) is not { AssinaturaDoDescobridor.Length: > 0 };
			c.Zone = z2;
			AoPisarNoMundo(c);
			Checa("um corpo SEM DONO (NPC, clone) nao descobre mundo: quem descobre e o primeiro JOGADOR que vier",
				  npcNaoDescobriu && _batismos.De(ChaveDe(z2))?.Descobridor == "Caio");
			Checa("...e o mundo de MESMO NOME e outra seed tem o registro DELE (a chave e a seed, nao o nome)",
				  z1.Name == z2.Name && ChaveDe(z1) != ChaveDe(z2) && _batismos.De(ChaveDe(z1))?.Descobridor == "Ana");

			a.Zone = terra;
			AoPisarNoMundo(a);
			Checa("planeta DO MAPA nao tem descobridor (a Terra ja estava la antes de qualquer personagem)",
				  _batismos.De(ChaveDe(terra)) == null);

			// A PORTA DE PRODUCAO: o `MoveToZone` com o host de verdade, pra um mundo gerado que existe na carta.
			if (AcharPlanetaGerado(SeedDoUniverso) is { } real)
			{
				ZoneKey zReal = Espaco.ZonaDe(real);
				falas = Ouvir(() => MoveToZone(pl.Id, zReal, new Vec2(0, 0)));
				Checa("**A PORTA DO `MoveToZone` ANOTA A DESCOBERTA**: o host chega num mundo gerado da carta e vira o descobridor dele",
					  _batismos.De(ChaveDe(zReal))?.Descobridor == pl.Name && Disse(falas, "primeiro a pisar"),
					  $"descobridor='{_batismos.De(ChaveDe(zReal))?.Descobridor}' | {Ultimos(falas)}");
				MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			}
			else Checa("(montagem) ha um planeta gerado na carta pra atravessar a porta de producao", false);

			// =====================================================================
			// 3) O BATISMO, PELO FUNIL DOS VERBOS
			// =====================================================================
			GD.Print("[batismo] -- 3) o batismo pelo funil dos verbos --");
			a.Zone = z1;
			falas = Pedir(a, "planeta_nomear", "  Bancadia  ");
			Checa("**O DESCOBRIDOR BATIZA**: o mundo passa a se chamar \"Bancadia\", por ela, e o servidor inteiro fica sabendo",
				  _batismos.De(ChaveDe(z1)) is { Nome: "Bancadia", PorQuem: "Ana", DireitoUsado: true }
				  && Disse(falas, "Ana batizou um mundo: Mundo de Bancada agora se chama Bancadia!"), Ultimos(falas));
			Checa("...e o servidor passa a FALAR o nome novo desse mundo (e so desse: o homonimo continua com o nome dele)",
				  NomeDoMundo(z1) == "Bancadia" && NomeDoMundo(z2) == "Mundo de Bancada");

			falas = Pedir(a, "planeta_nomear", "Bancadia Dois");
			Checa("...UMA VEZ SO: o segundo batismo do descobridor e recusado, com o motivo",
				  _batismos.De(ChaveDe(z1))?.Nome == "Bancadia" && Disse(falas, "já usou o seu batismo"), Ultimos(falas));

			falas = Pedir(b, "planeta_nomear", "Betolandia");
			Checa("QUEM NAO DESCOBRIU NEM DOMINA nao batiza, e ouve por que",
				  _batismos.De(ChaveDe(z1))?.Nome == "Bancadia" && Disse(falas, "só batiza um planeta quem pisou nele primeiro"), Ultimos(falas));

			// ---- A CONTRAPROVA: a regra de quem pode deixando de existir ----
			b.Zone = z3;
			Batismo.QualquerUmBatizaDeTeste = true;
			try { falas = Pedir(b, "planeta_nomear", "Terra de Ninguem"); }
			finally { Batismo.QualquerUmBatizaDeTeste = false; }
			Checa("(defeito injetado: qualquer um batiza) o MESMO pedido de quem nao tem direito passa -- a regua de cima sabe reprovar",
				  _batismos.De(ChaveDe(z3))?.Nome == "Terra de Ninguem", Ultimos(falas));
			b.Zone = z1;

			// ---- O DONO ----
			Dominio dominioDeBeto = Dominar(b, z1);
			falas = Pedir(a, "planeta_nomear", "Outra Vez");
			Checa("COM DONO NO PLANETA o descobridor nao da mais o nome: conquista fala mais alto que descoberta",
				  _batismos.De(ChaveDe(z1))?.Nome == "Bancadia" && Disse(falas, "domínio de Beto"), Ultimos(falas));

			falas = Pedir(b, "planeta_nomear", "Betolandia");
			Checa("o DONO tambem espera: o planeta acabou de ser batizado, e a troca tem intervalo",
				  _batismos.De(ChaveDe(z1))?.Nome == "Bancadia" && Disse(falas, "acabou de ser batizado"), Ultimos(falas));

			_batismos.De(ChaveDe(z1))!.Quando = TempoDoMundo - Batismo.SegundosEntreBatismos - 1;
			falas = Pedir(b, "planeta_nomear", "Betolandia");
			Checa("**O DONO DO DOMINIO BATIZA** (passado o intervalo): \"Bancadia\" vira \"Betolandia\", e o anuncio diz o nome de ANTES certo",
				  _batismos.De(ChaveDe(z1)) is { Nome: "Betolandia", PorQuem: "Beto" }
				  && Disse(falas, "Beto batizou um mundo: Bancadia agora se chama Betolandia!"), Ultimos(falas));

			// ---- O NOME QUE NAO SERVE NAO ENTRA ----
			_batismos.De(ChaveDe(z1))!.Quando = TempoDoMundo - Batismo.SegundosEntreBatismos - 1;
			List<string> recusas = Ouvir(() =>
			{
				Verbo(b, "planeta_nomear", "ab");
				Verbo(b, "planeta_nomear", "Namek");
				Verbo(b, "planeta_nomear", "Nome<b>");
				Verbo(b, "planeta_nomear", "terra de ninguem");   // o nome que o OUTRO mundo ja leva
				Verbo(b, "planeta_nomear", "Betolandia");          // o que este ja tem
			});
			Checa("nome curto, de mundo conhecido, com caractere proibido, de outro planeta ou igual ao atual: os cinco recusados, cada um com o seu motivo",
				  _batismos.De(ChaveDe(z1))?.Nome == "Betolandia" && recusas.Count == 5
				  && Disse(recusas, "de 3 a 24 caracteres") && Disse(recusas, "mundo conhecido") && Disse(recusas, "só letras")
				  && Disse(recusas, "já existe um planeta batizado") && Disse(recusas, "já se chama assim"), Ultimos(recusas));

			// ---- TIRAR O NOME ----
			falas = Pedir(a, "planeta_desnomear");
			Checa("TIRAR O NOME e do dono: o descobridor nao tira", _batismos.De(ChaveDe(z1))?.Nome == "Betolandia" && Disse(falas, "só quem domina"), Ultimos(falas));
			falas = Pedir(b, "planeta_desnomear");
			Checa("...e o dono tira: o mundo volta a se chamar o que se chamava, e o servidor anuncia",
				  _batismos.De(ChaveDe(z1)) is { Nome: "", PorQuem: "" } && NomeDoMundo(z1) == "Mundo de Bancada"
				  && Disse(falas, "Betolandia voltou a se chamar Mundo de Bancada"), Ultimos(falas));

			// ---- FORA DE PLANETA ----
			a.Zone = ZonaDoEspaco;
			falas = Pedir(a, "planeta_nomear", "Vazio");
			Checa("no ESPACO nao ha o que batizar", Disse(falas, "só dá pra batizar um planeta estando nele"), Ultimos(falas));
			a.Zone = z1;

			// =====================================================================
			// 4) O DOMINIO MUDA O DIREITO, E O FIO LEVA
			// =====================================================================
			GD.Print("[batismo] -- 4) o dominio muda o direito --");
			EscutaDeBatismos = [];
			MandarBatismos(a);
			MandarBatismos(b);
			MandarBatismos(pl);
			Checa("O FIO LEVA O DIREITO DE CADA UM sobre o chao em que esta: o dono, o descobridor ja gasto, e o admin",
				  EscutaDeBatismos.Exists(e => e.Quem == b.Id && e.Direito == Batismo.Direito.Dono)
				  && EscutaDeBatismos.Exists(e => e.Quem == a.Id && e.Direito == Batismo.Direito.Nenhum)
				  && EscutaDeBatismos.Exists(e => e.Quem == pl.Id && e.Direito == Batismo.Direito.Admin),
				  string.Join(" ", EscutaDeBatismos.Select(e => $"{e.Quem}:{e.Direito}")));
			Checa("...e a lista e a dos BATIZADOS (o mundo so descoberto nao viaja)",
				  EscutaDeBatismos.TrueForAll(e => e.Nomes == _batismos.Batizados.Count()) && _batismos.Batizados.Count() < _batismos.Quantos,
				  $"{_batismos.Batizados.Count()} batizado(s) de {_batismos.Quantos} no livro");

			EscutaDeBatismos = [];
			PerderDominio(dominioDeBeto, "", "bancada do batismo", anunciar: false);
			Checa("**PERDER O DOMINIO TIRA O DIREITO**, e o pacote sai na hora (a ponta `PerderDominio` do livro da conquista)",
				  EscutaDeBatismos.Exists(e => e.Quem == b.Id && e.Direito == Batismo.Direito.Nenhum),
				  string.Join(" ", EscutaDeBatismos.Select(e => $"{e.Quem}:{e.Direito}")));

			// O descobridor de z2 (Caio) nunca gastou o direito: com dono no planeta ele nao tem a vez, sem dono tem.
			c.Zone = z2;
			Dominio dominioEmZ2 = Dominar(b, z2);
			Batismo.Direito comDono = DireitoDeBatizarAqui(c, out _);
			PerderDominio(dominioEmZ2, "", "bancada do batismo", anunciar: false);
			Checa("...e o descobridor que ainda nao batizou VOLTA A TER A VEZ quando o planeta fica sem dono",
				  comDono == Batismo.Direito.Nenhum && DireitoDeBatizarAqui(c, out _) == Batismo.Direito.Descobridor);

			if (AcharPlanetaGerado(SeedDoUniverso) is { } carta)
			{
				EscutaDeBatismos = [];
				c.Zone = Espaco.ZonaDe(carta);
				Dominio fincado = FincarDominio(c, carta, new Vec2(0, 0));
				Checa("**DOMINAR DA O DIREITO**, e o pacote sai na hora (a ponta `FincarDominio`, a de toda vitoria)",
					  EscutaDeBatismos.Exists(e => e.Quem == c.Id && e.Direito == Batismo.Direito.Dono),
					  string.Join(" ", EscutaDeBatismos.Select(e => $"{e.Quem}:{e.Direito}")));
				_dominios.Remove(fincado);
			}

			// =====================================================================
			// 5) O NOME NA BOCA DO SERVIDOR, O DISCO E A VASSOURA
			// =====================================================================
			GD.Print("[batismo] -- 5) a boca do servidor, o disco e a vassoura --");
			falas = Pedir(pl, "planeta_nomear", "Terra Dois");
			Checa("**O ADMIN BATIZA QUALQUER PLANETA**, ate um do mapa: a Terra passa a se chamar \"Terra Dois\" na boca do servidor",
				  NomeDoPlaneta("Earth") == "Terra Dois" && NomeDoMundo(terra) == "Terra Dois"
				  && Disse(falas, $"{pl.Name} batizou um mundo: Terra agora se chama Terra Dois!"), Ultimos(falas));
			Checa("...e os OUTROS planetas do mapa continuam com o nome deles (Vegeta e Vegeta; a Terra era \"Terra\")",
				  NomeDoPlaneta("Vegeta") == "Vegeta" && NomeDeCatalogo("Earth") == "Terra");

			// O DISCO: grava, esquece e le de novo.
			SalvarBatismos();
			int batizadosAntes = _batismos.Batizados.Count(), noLivroAntes = _batismos.Quantos;
			_batismos.Limpar();
			CarregarBatismos();
			Checa("O LIVRO SOBREVIVE AO REINICIO: os nomes dados e os descobridores voltam do `planetas-nomes.json`",
				  _batismos.Batizados.Count() == batizadosAntes && _batismos.Quantos == noLivroAntes
				  && NomeDoPlaneta("Earth") == "Terra Dois" && _batismos.De(ChaveDe(z1))?.Descobridor == "Ana",
				  $"{_batismos.Batizados.Count()}/{batizadosAntes} batizados, {_batismos.Quantos}/{noLivroAntes} no livro");

			SistemaDoMundo? vassoura = _sistemasDoMundo.Find(s => s.Arquivos.Contains("planetas-nomes.json"));
			Checa("A VASSOURA DO WIPE CONHECE O LIVRO: o arquivo esta inscrito, e ela conta os BATIZADOS",
				  vassoura != null && vassoura.Contar() == _batismos.Batizados.Count() && vassoura.Contar() > 0,
				  vassoura == null ? "nao inscrito" : $"conta {vassoura.Contar()}");
			vassoura?.Zerar();
			Checa("...e zerar esvazia o livro (a Terra volta a ser a Terra)", _batismos.Quantos == 0 && NomeDoPlaneta("Earth") == "Terra");

			falas = Pedir(pl, "planeta_desnomear");
			Checa("tirar o nome de um planeta que nao tem nome dado e respondido, e nao inventa registro",
				  Disse(falas, "não tem nome dado") && _batismos.De(ChaveDe(terra)) is not { Nome.Length: > 0 }, Ultimos(falas));
		}
		finally
		{
			Batismo.QualquerUmBatizaDeTeste = false;
			EscutaDeBatismos = null;

			// O `Peer` EMPRESTADO NAO SAI DAQUI EM CORPO NENHUM, e a zona volta a ser a da bancada: a faxina tira
			// cada corpo da lista da zona em que ele DIZ estar, e a que eles foram postos e a Terra.
			foreach (ServerPlayer f in forjados) { f.Peer = null; f.Zone = ZonaDaBancadaDeProjetil; }
			LimparTudoDaBancada();

			_dominios.Clear();
			_dominios.AddRange(dominiosDeAntes);
			SalvarConquista();

			_batismos.Substituir(nomesDeAntes);
			try
			{
				if (arquivoDeAntes != null) System.IO.File.WriteAllBytes(CaminhoDosBatismos, arquivoDeAntes);
				else if (System.IO.File.Exists(CaminhoDosBatismos)) System.IO.File.Delete(CaminhoDosBatismos);
			}
			catch (Exception ex) { GD.PushWarning($"[bancada] nao deu pra devolver o livro dos nomes: {ex.Message}"); }

			if (!pl.Zone.Equals(zonaDeAntes)) MoveToZone(pl.Id, zonaDeAntes, posDeAntes); else pl.Pos = posDeAntes;
			MandarBatismos(pl);   // o cliente do host volta a ver a verdade (os corpos de bancada falaram pelo fio dele)

			GD.Print($"[batismo] ================ {ok} OK, {falhou} FALHA(S) ================\n");
		}
	}
}
