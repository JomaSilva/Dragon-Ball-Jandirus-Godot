using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DOS ITENS APOSENTADOS (`--aposentadosteste`) -- dono, 2026-10-10: *"o catalogo pode tirar essas armas
/// comentadas"*. Seis armas (Axe, Staff, Spear, Club, Hammer, Cross) estavam a venda na aba Tech sem existir no
/// jogo original; sairam do `construcoes.json`, e esta bancada cobra o que acontece com QUEM JA TINHA UMA.
///
/// E, desde 2026-10-11, o que NAO acontece -- dono: *"sem rembolso [...] ninguem fabricou item nenhum entao n
/// precisa de reembolso"*. O item sai com a frase que diz por que, e a carteira fica como estava.
///
/// No boot, como as outras de servidor. Os corpos sao forjados (sem `Peer`) e o save antigo passa pelo funil de
/// producao -- `AccountStore.ParaJogador`, o mesmo que o `Entrar` chama --, inclusive com uma ida e volta pelo
/// DISCO (`AccountStore.Gravar`/`Carregar` numa pasta temporaria).
///
///   1. A LISTA E O CATALOGO: as seis na lista, cada uma com o porque; cinco ids fora do catalogo, e o `Staff`
///      la como o cajado magico; nenhum id repetido no arquivo (e o alarme de boot sabe acusar um).
///   2. O LOGIN DE QUEM TINHA: o item sai, o ZENI NAO MUDA, o resto da mochila fica.
///   3. O RECADO: uma frase curta por item, com o nome e o porque, SEM falar em dinheiro -- uma vez so.
///   4. UMA VEZ SO: o save sai gravado na versao de hoje, e o segundo login (pelo disco) nao acha o que tirar.
///   5. O `Staff` DE DEPOIS FICA: fabricado pela bancada de hoje, e o cajado magico, e atravessa o relog.
///   6. QUEM NUNCA TEVE nao e tocado.
///
/// DOIS defeitos injetados, um pra cada lado da regra:
///   * `ItensAposentados.SomeCaladoDeTeste` e o jogo de antes -- o `Sanear` leva o item calado, sem frase, e o
///     `Staff` de antes fica na mochila como se fosse o cajado;
///   * `AposentadoDevolveZeniDeTeste` e o reembolso que o dono recusou -- a carteira sobe e a frase promete o
///     dinheiro.
///
///     Godot --headless -- --server --port 8270 --aposentadosteste
///
/// COM `--host` ela ganha uma setima familia, a VIVA (`[aposentadosvivo]`): o mesmo save de antes, logado pelo
/// `Entrar` de producao. Ver <see cref="SemearOVeteranoDoLoginVivo"/>.
/// </summary>
public partial class GameServer
{
	private int _apOk, _apFalhou;

	private void AfirmarAp(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _apOk++; GD.Print($"[aposentados]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _apFalhou++; GD.PrintErr($"[aposentados]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>
	/// O que a aba Tech cobrava por cada uma das seis armas (`cost=1000` em `Weapons.dm:18-113`). So serve ao
	/// contra-exemplo: e o que o defeito injetado do reembolso devolve por unidade.
	/// </summary>
	private const double PrecoQueAsArmasTinham = 1000;

	/// <summary>
	/// A FRASE FALA EM DINHEIRO? A regua do "sem reembolso" pro que o jogador LE: o recado de um item que saiu nao
	/// cita zeni nem promete devolver ou reembolsar nada. A mesma nas duas metades da bancada.
	/// </summary>
	private static bool RecadoFalaDeDinheiro(string frase) =>
		frase.Contains("zeni", StringComparison.OrdinalIgnoreCase) || frase.Contains("devolv", StringComparison.OrdinalIgnoreCase)
		|| frase.Contains("reembols", StringComparison.OrdinalIgnoreCase);

	public void RodarBancadaDosItensAposentados()
	{
		_apOk = _apFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[aposentados] ================ OS ITENS QUE SAIRAM DO JOGO (as armas comentadas, 2026-10-10) ================");

		string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jandirus_aposentados_" + Guid.NewGuid().ToString("N"));
		try
		{
			string[] idsQueSairam = ["Axe", "Spear", "Club", "Hammer", "Cross"];
			const double zeniDeAntes = 500;
			const int letrasDeUmRecadoCurto = 200;   // o teto do "frase curta": a do Staff, a mais longa, tem 188

			// ---------------------------------------------------------------- 1. a lista e o catalogo
			GD.Print("[aposentados] -- 1) a lista e o catalogo --");
			if (_obras == null) { AfirmarAp("ha catalogo de construcoes pra conferir", false); return; }

			AfirmarAp("as SEIS estao na lista dos aposentados, cada uma com o porque que o jogador vai ler (e nenhum porque fala em dinheiro), todas na versao de hoje",
					  ItensAposentados.Todos.Length == 6
					  && idsQueSairam.Append("Staff").All(id => ItensAposentados.Todos.Any(a => a.Id == id && a.Porque.Length > 0 && !RecadoFalaDeDinheiro(a.Porque)))
					  && ItensAposentados.Todos.Max(a => a.SaiuNaVersao) == ItensAposentados.VersaoAtual,
					  string.Join(", ", ItensAposentados.Todos.Select(a => $"{a.Id} v{a.SaiuNaVersao}")));

			List<string> aindaLa = [.. idsQueSairam.Where(id => _obras.Get(id) != null || CatalogoDeItens.Get(id) != null)];
			AfirmarAp("Axe, Spear, Club, Hammer e Cross NAO estao no catalogo que o jogo carrega, e nenhum deles vira item",
					  aindaLa.Count == 0, string.Join(", ", aindaLa));

			ServerPlayer comprador = Forjar("Comprador", CorredorLivre(2), 1000);
			comprador.Ficha.techskill = 100;
			comprador.Ficha.Zeni = 1_000_000;
			List<string> recusa = Ouvir(() => Construir(comprador, "Axe"));
			AfirmarAp("...e pedir pra fabricar um Axe e recusado como coisa que nao existe: nada e cobrado, nada entra na mochila",
					  Disse(recusa, "nao existe") && comprador.Ficha.Zeni == 1_000_000 && comprador.Mochila.Ocupados == 0, Ultimos(recusa));

			Construcao? cajado = _obras.Get("Staff");
			AfirmarAp("o `Staff` CONTINUA no catalogo, e e o cajado magico de `Magitech.dm:21-28` (tech 1, 1.000 zeni, com descricao) -- nao a arma de tech 11",
					  cajado is { Tech: 1, Custo: 1000, Tipo: "/obj/items/Equipment/Weapon/Staff/Magic_Staff", Construivel: true } && cajado.Desc.Length > 0,
					  cajado == null ? "fora do catalogo" : $"tech {cajado.Tech:0}, custo {cajado.Custo:0}, tipo '{cajado.Tipo}'");

			AfirmarAp("o `construcoes.json` nao traz id repetido: o alarme de boot nao tem o que dizer",
					  AvisarDosRepetidos(_obras) == 0, string.Join(", ", _obras.Repetidos));
			const string comRepetido =
				"""
				[
				  { "id": "Staff", "nome": "Staff", "custo": 1000, "tech": 1, "racas": [], "tipo": "/obj/items/Cajado" },
				  { "id": "Staff", "nome": "Staff", "custo": 1000, "tech": 11, "racas": [], "tipo": "" }
				]
				""";
			CatalogoDeObras sujo = CatalogoDeObras.Parse(comRepetido);
			AfirmarAp("(contra-exemplo do alarme: um arquivo com o `Staff` duas vezes) ele acusa UM repetido -- e o jogo ficaria com a ultima linha, a de tech 11",
					  AvisarDosRepetidos(sujo) == 1 && sujo.Repetidos.SequenceEqual(["Staff"]) && sujo.Total == 1 && sujo.Get("Staff")?.Tech == 11,
					  $"{string.Join(", ", sujo.Repetidos)}; tech que ficou: {sujo.Get("Staff")?.Tech}");

			// ---------------------------------------------------------------- o save de antes
			// Um machado, DOIS martelos (dois espacos: arma nao empilha), o Staff de antes -- e duas coisas que ficam.
			CharacterSave Antigo() => new()
			{
				Nome = "Veterano", Raca = "Human",
				Ficha = new Fighter { Race = "Human", Zeni = zeniDeAntes },
				Mochila = new Inventario
				{
					Pilhas =
					[
						new Pilha(CatalogoDeItens.Maca, 3), new Pilha("Axe", 1), new Pilha("Hammer", 1),
						new Pilha(CatalogoDeItens.Scouter, 1), new Pilha("Hammer", 1), new Pilha("Staff", 1),
					],
				},
				ItensVersao = 0,
			};

			// A REGUA: o que o login fez com aquele save. A mesma pra secao 2 e pro defeito injetado.
			(double Zeni, int Armas, int Staffs, int Macas, int Scouters, int Recados, List<string> Falas) Logar(ServerPlayer pl, CharacterSave save)
			{
				AccountStore.ParaJogador(save, pl);
				int recados = pl.ItensQueSairam.Count;
				List<string> falas = Ouvir(() => AvisarDosItensAposentados(pl));
				return (pl.Ficha.Zeni, idsQueSairam.Sum(pl.Mochila.Quantos), pl.Mochila.Quantos("Staff"),
						pl.Mochila.Quantos(CatalogoDeItens.Maca), pl.Mochila.Quantos(CatalogoDeItens.Scouter), recados, falas);
			}
			// AS TRES REGUAS do login de quem tinha. Separadas, porque cada defeito injetado quebra uma coisa diferente:
			//   SAIU       -- nenhuma das seis ficou na mochila (nem o Staff de antes), e cada perda teve o seu recado;
			//   SEM ZENI   -- a carteira e a de antes do login (a decisao do dono de 2026-10-11);
			//   SEM PROMESSA -- nenhuma frase fala em dinheiro.
			static bool SaiuEFoiDito((double Zeni, int Armas, int Staffs, int Macas, int Scouters, int Recados, List<string> Falas) m) =>
				m.Armas == 0 && m.Staffs == 0 && m.Recados == 3 && m.Falas.Count == 3;
			static bool CarteiraIntacta((double Zeni, int Armas, int Staffs, int Macas, int Scouters, int Recados, List<string> Falas) m) =>
				m.Zeni == zeniDeAntes;
			static bool SemPromessaDeDinheiro((double Zeni, int Armas, int Staffs, int Macas, int Scouters, int Recados, List<string> Falas) m) =>
				!m.Falas.Exists(RecadoFalaDeDinheiro);

			// ---------------------------------------------------------------- 2. o login de quem tinha
			GD.Print("[aposentados] -- 2) o login de quem tinha --");
			ServerPlayer veterano = Forjar("Veterano", CorredorLivre(2), 1000);
			var m = Logar(veterano, Antigo());
			AfirmarAp("**NO LOGIN O ITEM SAI**: o machado, os dois martelos e o Staff de antes saem da mochila, com um recado por perda",
					  SaiuEFoiDito(m), $"armas {m.Armas}, staffs {m.Staffs}, recados {m.Recados}");
			AfirmarAp("**E NAO VOLTA ZENI NENHUM** (dono: \"ninguem fabricou item nenhum entao n precisa de reembolso\"): a carteira entra com 500 e sai com 500",
					  CarteiraIntacta(m), $"zeni {zeniDeAntes:0} -> {m.Zeni:0}");
			AfirmarAp("...e o que nao saiu do jogo FICA: as tres macas e o scouter",
					  m.Macas == 3 && m.Scouters == 1 && veterano.Mochila.Ocupados == 2, $"macas {m.Macas}, scouter {m.Scouters}, espacos {veterano.Mochila.Ocupados}");

			// ---------------------------------------------------------------- 3. o recado
			GD.Print("[aposentados] -- 3) o recado --");
			string doMachado = m.Falas.Find(f => f.Contains("Axe")) ?? "";
			string dosMartelos = m.Falas.Find(f => f.Contains("Hammer")) ?? "";
			string doStaff = m.Falas.Find(f => f.Contains("Staff")) ?? "";
			AfirmarAp("**A PERDA E EXPLICADA**: a frase do machado diz o nome, que ele saiu do jogo e da mochila, e o PORQUE",
					  doMachado.Contains("o item Axe saiu do jogo") && doMachado.Contains("mochila")
					  && doMachado.Contains("arma que o jogo original deixou desativada"), doMachado);
			AfirmarAp("os DOIS martelos saem numa frase so",
					  dosMartelos.Contains("os 2 Hammer") && m.Falas.Count(f => f.Contains("Hammer")) == 1, dosMartelos);
			AfirmarAp("a frase do Staff avisa que o da aba Tech agora e OUTRO item (o cajado magico)",
					  doStaff.Contains("cajado mágico"), doStaff);
			AfirmarAp("**NENHUMA PROMETE DINHEIRO**: as tres frases nao falam em zeni, em devolver nem em reembolso",
					  m.Falas.Count == 3 && SemPromessaDeDinheiro(m), Ultimos(m.Falas));
			AfirmarAp($"...e sao CURTAS: nenhuma passa de {letrasDeUmRecadoCurto} letras (a mais longa e a do Staff, que explica os dois itens de mesmo nome)",
					  m.Falas.Count == 3 && m.Falas.TrueForAll(f => f.Length <= letrasDeUmRecadoCurto), string.Join(", ", m.Falas.Select(f => f.Length)));
			AfirmarAp("o recado sai UMA vez (a lista se esvazia ao falar)",
					  veterano.ItensQueSairam.Count == 0 && Ouvir(() => AvisarDosItensAposentados(veterano)).Count == 0);

			// ---------------------------------------------------------------- 4. uma vez so (pelo disco)
			GD.Print("[aposentados] -- 4) uma vez so: o save sai na versao de hoje --");
			var loja = new AccountStore(pasta);
			var conta = new AccountSave { Conta = "bancada_aposentados" };
			conta.Slots[0] = AccountStore.DeJogador(veterano, 0);
			loja.Gravar(conta);
			CharacterSave? doDisco = loja.Carregar("bancada_aposentados")?.Slots[0];
			AfirmarAp("o save de quem ja logou vai pro DISCO na versao de hoje da lista, com os mesmos 500 zeni",
					  doDisco is { ItensVersao: ItensAposentados.VersaoAtual } && doDisco.Ficha.Zeni == zeniDeAntes,
					  doDisco == null ? "nao voltou do disco" : $"versao {doDisco.ItensVersao}, zeni {doDisco.Ficha.Zeni:0}");
			if (doDisco != null)
			{
				ServerPlayer deNovo = Forjar("VeteranoDeNovo", CorredorLivre(2), 1000);
				var m2 = Logar(deNovo, doDisco);
				AfirmarAp("**O SEGUNDO LOGIN NAO ACHA O QUE TIRAR**: mesmo zeni, mesma mochila, nenhum recado",
						  m2.Zeni == zeniDeAntes && m2.Recados == 0 && m2.Falas.Count == 0 && m2.Macas == 3 && m2.Scouters == 1,
						  $"zeni {m2.Zeni:0}, recados {m2.Recados}");
			}

			// ---------------------------------------------------------------- 5. o Staff de depois fica
			GD.Print("[aposentados] -- 5) o Staff de DEPOIS e o cajado magico, e fica --");
			comprador.Ficha.techskill = 1;
			comprador.Ficha.Zeni = 1500;
			// (v0.2.1) O CAJADO MAGICO FECHOU PRA VENDA (`TechFechada`: ele pede o sistema de magia, e era vendido como um
			// item que nao faz nada): a bancada de hoje o recusa sem cobrar. Quem o comprou entre 2026-10-10 e a v0.2.1
			// continua com ele -- e e esse save que este passo monta, pondo o cajado na mochila pela mao.
			List<string> compra = Ouvir(() => Construir(comprador, "Staff"));
			AfirmarAp("a bancada de hoje NAO fabrica mais o Staff (o cajado magico fechou: pede o sistema de magia): nada entra na mochila e nenhum zeni sai",
					  comprador.Mochila.Quantos("Staff") == 0 && comprador.Ficha.Zeni == 1500 && compra.Exists(f => f.Contains("ainda não existe neste jogo")),
					  Ultimos(compra));
			comprador.Mochila.Guardar("Staff");
			comprador.Ficha.Zeni = 500;
			conta.Slots[1] = AccountStore.DeJogador(comprador, 0);
			loja.Gravar(conta);
			CharacterSave? comCajado = loja.Carregar("bancada_aposentados")?.Slots[1];
			if (comCajado == null) AfirmarAp("o save do comprador voltou do disco", false);
			else
			{
				ServerPlayer relogou = Forjar("CompradorDeNovo", CorredorLivre(2), 1000);
				var m3 = Logar(relogou, comCajado);
				AfirmarAp("**O RELOG NAO CONFISCA O CAJADO**: o Staff de quem comprou depois continua na mochila, sem zeni nem recado",
						  m3.Staffs == 1 && m3.Zeni == 500 && m3.Recados == 0 && m3.Falas.Count == 0, $"staffs {m3.Staffs}, zeni {m3.Zeni:0}, recados {m3.Recados}");
			}

			// ---------------------------------------------------------------- 6. quem nunca teve
			GD.Print("[aposentados] -- 6) quem nunca teve --");
			CharacterSave semNada = Antigo();
			semNada.Mochila.Pilhas.RemoveAll(p => p.Id is not (CatalogoDeItens.Maca or CatalogoDeItens.Scouter));
			ServerPlayer novato = Forjar("Novato", CorredorLivre(2), 1000);
			var m4 = Logar(novato, semNada);
			AfirmarAp("quem nunca teve uma das seis nao e tocado: 500 zeni, as macas e o scouter, nada pra avisar",
					  m4.Zeni == zeniDeAntes && m4.Macas == 3 && m4.Scouters == 1 && m4.Recados == 0 && m4.Falas.Count == 0, $"zeni {m4.Zeni:0}");

			// ---------------------------------------------------------------- os contra-exemplos
			GD.Print("[aposentados] -- os dois contra-exemplos --");
			ItensAposentados.SomeCaladoDeTeste = true;
			try
			{
				ServerPlayer calado = Forjar("Calado", CorredorLivre(2), 1000);
				var m5 = Logar(calado, Antigo());
				AfirmarAp("(defeito injetado: a limpeza nao roda -- o jogo de antes) o machado e os martelos SOMEM no `Sanear` sem frase, "
						  + "e o Staff de antes fica na mochila como se fosse o cajado: a regua \"saiu e foi dito\" da secao 2 REPROVA",
						  !SaiuEFoiDito(m5) && m5.Armas == 0 && m5.Recados == 0 && m5.Falas.Count == 0 && m5.Staffs == 1,
						  $"armas {m5.Armas}, staffs {m5.Staffs}, recados {m5.Recados}");
			}
			finally { ItensAposentados.SomeCaladoDeTeste = false; }

			// O REEMBOLSO QUE O DONO RECUSOU: os 1.000 zeni que a aba Tech cobrava por arma, de volta por unidade.
			AposentadoDevolveZeniDeTeste = PrecoQueAsArmasTinham;
			try
			{
				ServerPlayer reembolsado = Forjar("Reembolsado", CorredorLivre(2), 1000);
				var m6 = Logar(reembolsado, Antigo());
				AfirmarAp("(defeito injetado: o item que sai devolve o preco -- o reembolso recusado) os itens saem do mesmo jeito, mas a carteira vai de 500 a 4.500: "
						  + "a regua \"nao volta zeni nenhum\" da secao 2 REPROVA",
						  SaiuEFoiDito(m6) && !CarteiraIntacta(m6) && m6.Zeni == zeniDeAntes + 4 * PrecoQueAsArmasTinham, $"zeni {zeniDeAntes:0} -> {m6.Zeni:0}");
				AfirmarAp("(o mesmo defeito) as tres frases prometem o dinheiro, e a do Staff estoura o teto do recado curto: as reguas \"nenhuma promete dinheiro\" e \"sao curtas\" da secao 3 REPROVAM",
						  !SemPromessaDeDinheiro(m6) && m6.Falas.Count == 3 && m6.Falas.TrueForAll(RecadoFalaDeDinheiro)
						  && m6.Falas.Exists(f => f.Length > letrasDeUmRecadoCurto), Ultimos(m6.Falas));
			}
			finally { AposentadoDevolveZeniDeTeste = 0; }

			// A METADE VIVA, quando ha um host pra logar: o save de antes fica semeado na conta dele.
			SemearOVeteranoDoLoginVivo(Antigo());
		}
		catch (Exception e)
		{
			AfirmarAp("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			ItensAposentados.SomeCaladoDeTeste = false;
			AposentadoDevolveZeniDeTeste = 0;
			try { if (System.IO.Directory.Exists(pasta)) System.IO.Directory.Delete(pasta, recursive: true); }
			catch (Exception e) { GD.Print($"[aposentados] nao consegui apagar {pasta}: {e.Message}"); }
			LimparTudoDaBancada();
			GD.Print($"[aposentados] ================ {_apOk} OK, {_apFalhou} FALHA(S) ================");
		}
	}

	/// <summary>
	/// ============================ A METADE VIVA: O `Entrar` DE VERDADE ============================
	/// As familias de cima chamam o `ParaJogador` e o recado cada um por si. O que elas NAO provam e que o login de
	/// producao amarra os dois -- carregar, limpar, gravar, falar --, nem que um save com id que o catalogo nao
	/// conhece atravessa o `Entrar` inteiro sem quebrar.
	///
	/// Entao, com `--host` (a bancada sobe com `--conta X`), o save de antes e GRAVADO NA CONTA DO HOST antes de ele
	/// conectar: o cliente automatico acha o slot ocupado e joga nele (`Boot.AutoEscolher`), e o personagem passa
	/// pelo `Entrar` como o de qualquer jogador que volta depois da atualizacao. A escuta fica ligada do boot ate a
	/// conferencia, que roda fora do `Entrar` (um relogio de meio em meio segundo) e olha o corpo VIVO, o que o
	/// servidor disse a ele e o que ficou no DISCO.
	///
	///     Godot --headless -- --host --rede 8271 --aposentadosteste --raca Human --conta bancada_aposentados_vivo --nome Veterano
	/// ============================================================================================
	/// </summary>
	private void SemearOVeteranoDoLoginVivo(CharacterSave antigo)
	{
		string[] args = OS.GetCmdlineArgs();
		int i = Array.IndexOf(args, "--conta");
		if (Array.IndexOf(args, "--host") < 0 || i < 0 || i + 1 >= args.Length || _store == null) return;
		string conta = args[i + 1];
		double zeniDeAntes = antigo.Ficha.Zeni;

		// O SAVE: o de um corpo de verdade (zona, posicao, membros), com a mochila e a carteira do veterano.
		ServerPlayer molde = Forjar("Veterano", CorredorLivre(2), 1000);
		CharacterSave semente = AccountStore.DeJogador(molde, NowMs());
		semente.Nome = "Veterano";
		semente.Planeta = "Earth";
		semente.Historia = "Personagem de bancada: jogava antes de as armas comentadas sairem do catalogo.";
		semente.CriadoEm = NowMs();
		semente.Ficha = new Fighter { Race = "Human", BP = 1000, Zeni = zeniDeAntes };
		semente.Ficha.Class = "Normal";
		semente.Mochila = antigo.Mochila;
		semente.ItensVersao = 0;
		var acc = new AccountSave { Conta = conta, CriadaEm = NowMs() };
		acc.Slots[0] = semente;
		_store.Gravar(acc);
		GD.Print($"[aposentadosvivo] save de antes semeado na conta '{conta}': {string.Join(", ", semente.Mochila.Pilhas.Select(p => $"{p.Id}x{p.Quantidade}"))}, {zeniDeAntes:0} zeni, versao 0");

		List<string> falas = [];
		EscutaDeAvisos = falas;
		long prazo = NowMs() + 90_000;
		int ok = 0, falhou = 0;
		void Afirmar(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[aposentadosvivo]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[aposentadosvivo]   FALHA {nome}   [{detalhe}]"); }
		}

		void Olhar()
		{
			ServerPlayer? vivo = _players.Values.FirstOrDefault(p => p.Peer != null && string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase));
			if (vivo == null && NowMs() < prazo) { GetTree().CreateTimer(0.5).Timeout += Olhar; return; }

			EscutaDeAvisos = null;
			try
			{
				if (vivo == null) { Afirmar("o host logou no personagem semeado em 90 s", false); return; }

				string[] armas = ["Axe", "Spear", "Club", "Hammer", "Cross", "Staff"];
				Afirmar("**O LOGIN NAO QUEBRA**: o host entrou no mundo pelo `Entrar`, no personagem do save de antes",
						vivo.Name == "Veterano" && vivo.Race == "Human", $"{vivo.Name}, {vivo.Race}");
				Afirmar("o corpo VIVO nao carrega mais nenhuma das seis, e ficou com as macas e o scouter",
						armas.Sum(vivo.Mochila.Quantos) == 0 && vivo.Mochila.Quantos(CatalogoDeItens.Maca) == 3 && vivo.Mochila.Quantos(CatalogoDeItens.Scouter) == 1,
						string.Join(", ", vivo.Mochila.Pilhas.Select(p => $"{p.Id}x{p.Quantidade}")));
				Afirmar("**E A CARTEIRA NAO MUDOU** (dono: \"ninguem fabricou item nenhum entao n precisa de reembolso\"): os 500 zeni do save, nenhum de volta",
						vivo.Ficha.Zeni == zeniDeAntes, $"zeni {zeniDeAntes:0} -> {vivo.Ficha.Zeni:0}");

				List<string> recados = [.. falas.Where(f => f.Contains("saiu do jogo") || f.Contains("saíram do jogo"))];
				Afirmar("**O `Entrar` FALOU**: os tres recados (o machado, os dois martelos, o Staff) sairam durante o login, uma vez cada, e a lista esvaziou",
						recados.Count == 3 && recados.Count(f => f.Contains("Axe")) == 1 && recados.Count(f => f.Contains("os 2 Hammer")) == 1
						&& recados.Count(f => f.Contains("cajado mágico")) == 1 && vivo.ItensQueSairam.Count == 0, Ultimos(recados));
				Afirmar("...e nenhum dos tres promete dinheiro: nao falam em zeni, em devolver nem em reembolso",
						recados.Count == 3 && !recados.Exists(RecadoFalaDeDinheiro), Ultimos(recados));

				CharacterSave? noDisco = _store.Carregar(conta)?.Slots[0];
				Afirmar("**E GRAVOU**: o save no disco ja esta na versao de hoje, sem as armas e com os mesmos 500 zeni -- o proximo login nao acha o que tirar",
						noDisco is { ItensVersao: ItensAposentados.VersaoAtual } && noDisco.Ficha.Zeni == zeniDeAntes
						&& armas.Sum(noDisco.Mochila.Quantos) == 0,
						noDisco == null ? "sem save" : $"versao {noDisco.ItensVersao}, zeni {noDisco.Ficha.Zeni:0}, mochila {string.Join(", ", noDisco.Mochila.Pilhas.Select(p => p.Id))}");

				// O CONTRA-EXEMPLO, NO CORPO VIVO: o machado "sai" outra vez pelo recado de producao, agora com o botao
				// do reembolso ligado. (O host loga uma vez so por processo; o recado e o trecho do `Entrar` por onde
				// a regra do "sem reembolso" passa, e e ele que roda aqui, sobre o corpo que acabou de logar.)
				double carteira = vivo.Ficha.Zeni;
				AposentadoDevolveZeniDeTeste = PrecoQueAsArmasTinham;
				try
				{
					vivo.ItensQueSairam = [(ItensAposentados.Todos[0], 1)];
					List<string> comDinheiro = Ouvir(() => AvisarDosItensAposentados(vivo));
					Afirmar("(defeito injetado: o item que sai devolve o preco -- o reembolso recusado) no corpo vivo a carteira sobe 1.000 e a frase promete o dinheiro: "
							+ "as reguas \"a carteira nao mudou\" e \"nenhum promete dinheiro\" REPROVAM",
							vivo.Ficha.Zeni == carteira + PrecoQueAsArmasTinham && comDinheiro.Count == 1 && comDinheiro.TrueForAll(RecadoFalaDeDinheiro),
							$"zeni {carteira:0} -> {vivo.Ficha.Zeni:0}; {Ultimos(comDinheiro)}");
				}
				finally { AposentadoDevolveZeniDeTeste = 0; vivo.Ficha.Zeni = carteira; vivo.ItensQueSairam = []; }
			}
			catch (Exception e) { Afirmar("a conferencia do login vivo rodou ate o fim", false, e.ToString()); }
			finally { GD.Print($"[aposentadosvivo] ================ {ok} OK, {falhou} FALHA(S) ================"); }
		}
		GetTree().CreateTimer(0.5).Timeout += Olhar;
	}
}
