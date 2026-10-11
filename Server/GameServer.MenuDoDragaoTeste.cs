using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO MENU DO DRAGAO (`--menudodragaoteste`) -- a janela de desejos, do lado do servidor.
///
/// ============================ O QUE SO DAQUI SE RESPONDE ============================
///   1. **A JANELA ABRE SOZINHA?** O dragao sobe e quem invocou recebe o menu com `abrir`, o nome do dragao, os
///      pedidos da ativacao e o prazo -- e quem esta ao lado nao recebe nada.
///   2. **A LISTA E A DO SERVIDOR?** As linhas sao as da tabela (`Desejos.Menu`), o que da pra pedir na frente
///      e o apagado depois; o nao-portado vem apagado com o `Falta`; o criador ve "Poder" apagado; os Marcos
///      apagam pra quem ja pediu.
///   3. **OS PEDIDOS POR INVOCACAO SAO RESPEITADOS?** Tres pedidos sao tres: o tique de segundo nao devolve
///      pedido gasto (o desejo ilimitado medido em 2026-10-09), e o quarto e recusado.
///   4. **CADA DESEJO, COM E SEM ALVO, PELO CAMINHO DA JANELA?** A bancada faz o que a janela faz: le o menu,
///      acha a linha, acha a resposta da segunda etapa e manda o verbo e o argumento que vieram nela, pelo
///      funil dos verbos (`Verbo`). Os com alvo um a um (juventude pra mim e pra outro, ressuscitar, matar,
///      curar um planeta, o corpo de um Saiyajin); os sem alvo todos, num laco (secao 5b); e uma trava de
///      conjunto: TODO desejo portado da tabela tem de ter sido concedido por esse caminho. As linhas apagadas
///      tambem todas: mandadas assim mesmo (o cliente mexido), nao gastam pedido.
///   5. **"PRA QUEM?" OFERECE QUEM O DESEJO ALCANCA?** Quem esta ao lado entra; quem esta longe, quem morreu e
///      quem morreu de velhice ficam de fora da lista certa; o alvo MARCADO vem na frente.
///   6. **ALVO INVALIDO E RECUSADO?** O id de quem nao esta na lista nao consome pedido nem toca em ninguem.
///   7. **A JANELA SE ACERTA E FECHA?** Um menu novo quando algo muda, nenhum quando nada muda, e o aviso de
///      fechar quando o dragao vai embora (pedidos gastos, dispensado, prazo).
///   8. **O SUPER SHENRON PELO MESMO CAMINHO?** Emprestar as sete (com a lista de quem esta ao lado), aceitar a
///      guarda, registrar o pedido, pronunciar, o preco do desejo supremo -- tudo por linha de menu.
///   9. **O FIO E FIEL?** Todo pacote que saiu se le inteiro (`FioDoMenuDoDragao`) e da o mesmo menu.
/// ================================================================================
///
/// ============================ AS FAMILIAS COM DEFEITO INJETADO ============================
///   A. `DragaoSobeSemJanelaDeTeste`     -> o dragao sobe e a janela nao abre (o que o port fazia: a lista no chat).
///   B. `MenuNaoApagaNadaDeTeste`        -> o menu oferece ao criador o pedido que encerraria a invocacao dele.
///   C. `ContagemZeraTodoTiqueDeTeste`   -> o tique devolve o pedido gasto: cinco concedidos num set de tres.
///   D. `DragaoNaoRenovaAEsperaDeTeste`  -> o pedido concedido nao devolve a espera: o dragao some no meio.
///   E. `AlvoForaDaListaDeTeste`         -> o id de quem esta longe e aceito.
///   F. `DragaoSomeCaladoDeTeste`        -> o dragao vai embora e a janela nao e avisada.
///   G. `MenuSaiTodoTiqueDeTeste`        -> um menu por tique, mudando ou nao.
///   H. `ListaNaOrdemDaTabelaDeTeste`    -> a lista abre em "Nada" e numa "Calcinha" apagada, antes do que da pra pedir.
///   I. `GestoDeOutroMundoDeTeste`       -> "Pedidos por invocação" aparece em outro mundo, onde mexeria na estatua errada.
/// ========================================================================================
///
///     Godot --headless --path . --host --rede 8180 --conta bancada_menudragao --senha teste
///            --nome MenuBanca --raca Namekian --menudodragaoteste
/// </summary>
public partial class GameServer
{
	private bool _menuDoDragaoDeTeste;

	/// <summary>Roda uma vez, no primeiro login. MEXE no mundo e no disco -- so com a flag.</summary>
	private void RodarBancadaDoMenuDoDragao(ServerPlayer pl)
	{
		GD.Print("\n[menudragao] ================ O MENU DO DRAGAO: a janela de desejos, do lado do servidor ================");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[menudragao]   OK    {nome}"); }
			else { falhou++; GD.PrintErr($"[menudragao]   FALHA {nome}   [{detalhe}]"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		// Ela ergue uma estatua, mata e ressuscita gente, forja corpos, troca a raca do testador, mexe nos
		// tronos, adianta o relogio do ceu, da e tira Super Esferas e escreve nos dois arquivos das esferas.
		// Tudo fotografado aqui e devolvido no `finally` -- o molde da `--desejoteste`.
		// ==============================================================================
		var setsGuardados = new List<SetDeEsferas>(_sets);
		var esferasGuardadas = new List<Esfera>(_esferas);
		var supersGuardadas = _supers.Select(s => (s.Numero, s.Dono, s.DonoNome)).ToList();
		int cicloGuardado = _cicloDasSupers;
		(string bs, string bn, string bp2, string ba) benefGuardado = (_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido);
		double ceuGuardado = _adiantoDoCeu;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		string racaGuardada = pl.Race, classeGuardada = pl.Class;
		double bpGuardado = pl.Ficha.BP, zeniGuardado = pl.Ficha.Zeni;
		int idadeGuardada = pl.Idade, alvoGuardado = pl.AlvoId, marcosGuardados = pl.Ficha.wishedpoints;
		bool linguaGuardada = pl.Ficha.godtongue;
		var tronosGuardados = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);
		var forjados = new List<ServerPlayer>();
		List<string>? escutaDeAvisosAntes = EscutaDeAvisos;

		var fio = new List<(int Para, MenuDoDragao Menu, byte[] Fio)>();
		EscutaDosMenusDoDragao = fio;
		EscutaDosDesejos = [];
		EscutaDasSupers = [];

		const int T = ZoneCollision.TileSize;

		try
		{
			// =====================================================================
			// 0. O PALCO: um set de TRES pedidos na Terra, erguido pelo testador
			// =====================================================================
			_sets.RemoveAll(s => !s.Eterno);
			_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
			_invocacoes.Clear();
			_olhandoODragao.Clear();

			var terra = ZoneKey.Premade("Earth");
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			pl.Race = "Namekian";
			pl.Class = "Dragon clan";
			_tronos["guardian"] = pl.Conta;
			pl.Ficha.BP = 5_000_000;
			pl.Ficha.wishedpoints = 0;
			pl.Ficha.Statify();
			pl.AlvoId = 0;

			ErguerEstatua(pl, "");
			SetDeEsferas? set = _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);
			Checa("(montagem) o verbo de producao ergueu a estatua e as sete", set != null && _esferas.Count(e => e.Set == set.Id) == Esferas.Total);
			if (set == null) throw new InvalidOperationException("sem set: o resto da bancada nao roda");

			set.Desejos = 3;
			_adiantoDoCeu += (set.AtivoEm - TempoDoMundo) + 5;
			// O TIQUE DE PRODUCAO VE O SET ACORDADO antes de qualquer pedido, como em jogo (ele roda de segundo em
			// segundo, e ninguem reune sete esferas em menos que isso).
			TickDasEsferas();
			Checa("(montagem) passada a espera de nascimento o set esta acordado, com 3 pedidos", SetAtivo(set) && set.Desejos == 3 && set.Pedidos == 0);
			double poderDoSet = set.Poder;

			// ---- as pessoas do palco. Conta e slot nao sao enfeite: sem eles nao ha assinatura (`Forjar`).
			ServerPlayer vizinho = Forjar("Kuririn", pl.Pos, terra, "bancada_menu_b");
			ServerPlayer longe = Forjar("Yamcha", new Vec2(pl.Pos.X + 12 * T, pl.Pos.Y), terra, "bancada_menu_c");
			ServerPlayer caido = Forjar("Tenshinhan", pl.Pos, terra, "bancada_menu_d");
			ServerPlayer velho = Forjar("Mestre Kame", pl.Pos, terra, "bancada_menu_e");
			forjados.AddRange([vizinho, longe, caido, velho]);
			caido.Combate.Morrer(ignorarSeguro: true);
			velho.Combate.Morrer(ignorarSeguro: true);
			velho.Ficha.aged_out = true;

			// ---- o que a JANELA faz, na bancada
			// O menu que a janela deste jogador mostraria agora (montado, e nao mandado).
			MenuDoDragao MenuDe(ServerPlayer quem, QualDragao qual = QualDragao.Nenhum) => RetratoDoDragaoDe(quem, qual).Menu;

			// O que saiu no fio pra alguem desde uma marca.
			List<MenuDoDragao> Desde(int marca, ServerPlayer quem) =>
				[.. fio.Skip(marca).Where(x => x.Para == quem.Id).Select(x => x.Menu)];

			// OS DESEJOS DO DRAGAO COMUM QUE ESTA BANCADA PEDIU PELO CAMINHO DA JANELA E VIU SER CONCEDIDOS (a contagem
			// do set andou um). A secao 5b cobra que sejam TODOS os portados da tabela.
			var pelaJanela = new HashSet<string>(StringComparer.Ordinal);
			void Mandar(ServerPlayer quem, string verbo, string pedido)
			{
				int antes = set.Pedidos;
				Verbo(quem, verbo, pedido);
				if (verbo == "db_desejar" && set.Pedidos == antes + 1) pelaJanela.Add(pedido.Split(' ')[0]);
			}

			// A JANELA PEDINDO: acha a linha no menu de agora; se a linha pergunta, acha a resposta pelo rotulo;
			// e manda o verbo e o argumento que VIERAM NO MENU, pelo funil dos verbos. Falso = a linha (ou a
			// resposta) nao esta la ou esta apagada -- e nada saiu, como na janela.
			bool Pedir(ServerPlayer quem, string linha, string? resposta = null, QualDragao qual = QualDragao.Nenhum)
			{
				if (MenuDe(quem, qual).Entrada(linha) is not { Pode: true } e) return false;
				string pedido = e.Pedido;
				if (e.TemPergunta)
				{
					if (e.Escolhas.Find(c => c.Rotulo == resposta) is not { Pode: true } c) return false;
					pedido = c.Pedido;
				}
				Mandar(quem, e.Verbo, pedido);
				return true;
			}

			// UM DRAGAO NOVO DE PE pra alguem: fecha o que houver, deixa o relogio (e o tique de producao) acordar
			// o set se ele dormia, junta as sete e invoca pelo verbo de producao.
			void DragaoPara(ServerPlayer quem)
			{
				FecharAInvocacao(set.Id, "");
				if (!SetAtivo(set)) _adiantoDoCeu += Math.Max(0, set.AtivoEm - TempoDoMundo) + 5;
				TickDasEsferas();
				set.Pedidos = 0;   // a contagem PARCIAL de uma cena anterior nao e desta
				foreach (Esfera e in _esferas.Where(x => x.Set == set.Id))
				{
					e.PorZona(quem.Zone);
					e.Portador = quem.Id;
					e.X = quem.Pos.X;
					e.Y = quem.Pos.Y;
				}
				InvocarODragao(quem);
			}

			// =====================================================================
			// 1. A JANELA ABRE SOZINHA -- familia A
			// =====================================================================
			GD.Print("[menudragao] -- 1. o dragao sobe e a janela abre --");
			bool AbreSozinha()
			{
				_olhandoODragao.Clear();
				int antes = fio.Count;
				DragaoPara(pl);
				return _invocacoes.ContainsKey(set.Id)
					&& fio.Skip(antes).Any(x => x.Para == pl.Id && x.Menu is { Abrir: true, DePe: true, Fechar: false });
			}

			MutacaoDeDesejo(Checa,
				"o dragao sobe e a janela de desejos ABRE SOZINHA pra quem invocou (o pedido do dono)",
				"o dragao sobe e nao manda janela nenhuma -- ate 2026-10-10 a lista ia pro chat",
				AbreSozinha,
				() => DragaoSobeSemJanelaDeTeste = true,
				() => DragaoSobeSemJanelaDeTeste = false);

			MenuDoDragao aberto = fio.Last(x => x.Para == pl.Id).Menu;
			Checa("...com o dragao certo, os pedidos desta ativacao e o prazo que ele espera",
				  aberto is { Dragao: QualDragao.Comum, Restam: 3, Total: 3, Segundos: > 100 and <= 120 } && aberto.Nome == set.NomeDoDragao,
				  $"{aberto.Dragao} '{aberto.Nome}' {aberto.Restam}/{aberto.Total}, {aberto.Segundos}s");
			Checa("...e SO pra ele: quem esta ao lado nao recebe janela nenhuma", !fio.Exists(x => x.Para == vizinho.Id));

			// =====================================================================
			// 2. A LISTA E A DO SERVIDOR -- familia B
			// =====================================================================
			GD.Print("[menudragao] -- 2. a lista, os apagados e os gestos --");
			string[] daTabela = [.. MenuDaqui(pl, set).Select(d => d.Id).Where(id => id is not ("cancelar" or "juventude_alheia"))];
			string[] naJanela = [.. aberto.Entradas.Where(e => !e.Acao).Select(e => e.Id)];
			Checa("as linhas sao as da tabela do servidor (`Desejos.Menu`): nenhuma a mais, nenhuma a menos",
				  daTabela.Length > 8 && daTabela.Order().SequenceEqual(naJanela.Order()), $"tabela [{string.Join(",", daTabela)}] janela [{string.Join(",", naJanela)}]");

			// A ORDEM DA JANELA: o que da pra pedir primeiro (na ordem da tabela, com "nada" fechando o grupo), o
			// apagado depois (na ordem da tabela), e os gestos no fim.
			bool PediveisPrimeiro()
			{
				MenuDoDragao m = MenuDe(pl);
				string[] acesas = [.. daTabela.Where(id => m.Entrada(id) is { Pode: true })];
				string[] esperada = [.. acesas.Where(id => id != "nada"), .. acesas.Where(id => id == "nada"),
									 .. daTabela.Where(id => m.Entrada(id) is { Pode: false })];
				int primeiroGesto = m.Entradas.FindIndex(e => e.Acao);
				return acesas.Length > 3 && acesas.Length < daTabela.Length && esperada[acesas.Length - 1] == "nada"
					&& m.Entradas.Where(e => !e.Acao).Select(e => e.Id).SequenceEqual(esperada)
					&& primeiroGesto > 0 && m.Entradas.Skip(primeiroGesto).All(e => e.Acao);
			}

			MutacaoDeDesejo(Checa,
				"a lista comeca pelo que DA PRA PEDIR (com \"nada\" por ultimo entre eles); o apagado vem depois, e os gestos no fim",
				"a lista sai na ordem da tabela do DM: abre em \"Nada\" e numa \"Calcinha\" apagada",
				PediveisPrimeiro,
				() => ListaNaOrdemDaTabelaDeTeste = true,
				() => ListaNaOrdemDaTabelaDeTeste = false);
			Checa("...'cancelar' virou o gesto de dispensar, e 'rejuvenescer outro' mora dentro de 'Juventude'",
				  MenuDaqui(pl, set).Any(d => d.Id == "juventude_alheia") && aberto.Entrada("juventude_alheia") == null
				  && aberto.Entrada("cancelar") == null && aberto.Entrada(AcoesDoDragao.Dispensar) is { Acao: true, Pedido: "cancelar" });

			List<DesejoDef> naoPortados = [.. MenuDaqui(pl, set).Where(d => !d.Portado)];
			Checa($"os {naoPortados.Count} desejos NAO PORTADOS aparecem APAGADOS, cada um com o motivo dele",
				  naoPortados.Count > 0 && naoPortados.All(d => aberto.Entrada(d.Id) is { Pode: false } e && e.Motivo.Contains(d.Falta, StringComparison.Ordinal)),
				  string.Join(", ", naoPortados.Where(d => aberto.Entrada(d.Id) is not { Pode: false }).Select(d => d.Id)));
			Checa("...e um portado sem impedimento aparece ACESO e sem pergunta (senao 'apagado' seria a lista inteira)",
				  aberto.Entrada("dinheiro") is { Pode: true, TemPergunta: false, Verbo: "db_desejar", Pedido: "dinheiro" });

			MutacaoDeDesejo(Checa,
				"o CRIADOR ve 'Poder' apagado, com o motivo: pedir encerraria a invocacao dele (`WishTable.dm:207`)",
				"o menu nao apaga linha nenhuma",
				() => MenuDe(pl).Entrada("poder") is { Pode: false, Motivo.Length: > 20 },
				() => MenuNaoApagaNadaDeTeste = true,
				() => MenuNaoApagaNadaDeTeste = false);

			pl.Ficha.wishedpoints = Desejos.MarcosDoDesejo;
			bool marcosApagado = MenuDe(pl).Entrada("marcos") is { Pode: false, Motivo.Length: > 10 };
			pl.Ficha.wishedpoints = 0;
			Checa("quem JA pediu Marcos ve a linha apagada, e quem nunca pediu a ve acesa",
				  marcosApagado && MenuDe(pl).Entrada("marcos") is { Pode: true });

			Checa("os gestos: dispensar (quem invocou), pedidos por invocacao (quem ergueu) e derrubar (quem esta ao lado da estatua)",
				  aberto.Entrada(AcoesDoDragao.Pedidos) is { Acao: true, Perigo: true, Verbo: "db_refazer", Escolhas.Count: 3 }
				  && aberto.Entrada(AcoesDoDragao.Derrubar) is { Acao: true, Perigo: true, Verbo: "db_derrubar", Pedido: "sim" },
				  string.Join(",", aberto.Entradas.Where(e => e.Acao).Select(e => e.Id)));

			// =====================================================================
			// 3. OS PEDIDOS POR INVOCACAO -- familias C e D
			// =====================================================================
			GD.Print("[menudragao] -- 3. a contagem, o tique e a espera do dragao --");
			double zeniAntes = pl.Ficha.Zeni;
			int marca = fio.Count;
			bool pediu = Pedir(pl, "dinheiro");
			Checa("RIQUEZA pelo caminho da janela (linha sem pergunta) e concedida, e gasta UM pedido",
				  pediu && Math.Abs(pl.Ficha.Zeni - (zeniAntes + Desejos.ZeniDoDesejo)) < 1 && set.Pedidos == 1,
				  $"pediu={pediu}, zeni {zeniAntes:N0} -> {pl.Ficha.Zeni:N0}, pedidos {set.Pedidos}");
			Checa("...e a janela de quem pediu recebe a contagem nova NA HORA: restam 2 de 3, sem mandar abrir de novo",
				  Desde(marca, pl).LastOrDefault() is { Restam: 2, Total: 3, Abrir: false, DePe: true, Fechar: false },
				  $"{Desde(marca, pl).Count} pacote(s)");

			// A FAMILIA C: o desejo ilimitado. Cinco "nada" seguidos, um tique de producao depois de cada um.
			bool SoTresPedidos()
			{
				DragaoPara(pl);
				double poder = set.Poder;
				int concedidos = 0;
				for (int i = 0; i < 5; i++)
				{
					Verbo(pl, "db_desejar", "nada");
					if (set.Poder > poder * 1.0001) { concedidos++; poder = set.Poder; }
					TickDasEsferas();
				}
				set.Poder = poderDoSet;
				GD.Print($"[menudragao]   [medido] 5 pedidos em 5 segundos num set de 3: {concedidos} concedido(s), "
					   + $"dragao {(_invocacoes.ContainsKey(set.Id) ? "DE PE" : "foi embora")}");
				return concedidos == 3 && !_invocacoes.ContainsKey(set.Id);
			}

			MutacaoDeDesejo(Checa,
				"um set de TRES pedidos da TRES: o tique de segundo nao devolve pedido gasto, e o dragao vai embora no terceiro",
				"a contagem volta a zero em todo tique de set acordado -- o desejo ilimitado medido em 2026-10-09",
				SoTresPedidos,
				() => ContagemZeraTodoTiqueDeTeste = true,
				() => ContagemZeraTodoTiqueDeTeste = false);

			DragaoPara(pl);
			Verbo(pl, "db_desejar", "nada");
			for (int i = 0; i < 3; i++) TickDasEsferas();
			Checa("...e a contagem PARCIAL atravessa os tiques: um pedido gasto, tres segundos depois restam dois",
				  set.Pedidos == 1 && MenuDe(pl) is { Restam: 2, DePe: true }, $"pedidos {set.Pedidos}");
			Pedir(pl, AcoesDoDragao.Dispensar);
			DragaoParaSemZerar();
			Checa("...e atravessa a INVOCACAO seguinte: dispensado com 2 restando, o dragao volta com 2",
				  _invocacoes.ContainsKey(set.Id) && MenuDe(pl) is { Restam: 2, Total: 3, DePe: true }, $"pedidos {set.Pedidos}");
			set.Poder = poderDoSet;

			void DragaoParaSemZerar()
			{
				foreach (Esfera e in _esferas.Where(x => x.Set == set.Id))
				{
					e.PorZona(pl.Zone);
					e.Portador = pl.Id;
					e.X = pl.Pos.X;
					e.Y = pl.Pos.Y;
				}
				InvocarODragao(pl);
			}

			// A FAMILIA D: a espera do dragao volta inteira a cada pedido concedido.
			bool OPedidoDevolveAEspera()
			{
				DragaoPara(pl);
				_invocacoes[set.Id].ExpiraEm = TempoDoMundo + 10;   // ele ja esperou 110 dos 120 s
				Verbo(pl, "db_desejar", "nada");                    // concedido: restam dois
				_adiantoDoCeu += 60;
				TickDasEsferas();
				set.Poder = poderDoSet;
				return set.Pedidos == 1 && _invocacoes.ContainsKey(set.Id);
			}

			MutacaoDeDesejo(Checa,
				"cada pedido concedido devolve a espera do dragao: 60 s depois do primeiro ele ainda esta de pe",
				"o prazo conta so da invocacao -- o set de tres pedidos tem os mesmos 120 s do de um",
				OPedidoDevolveAEspera,
				() => DragaoNaoRenovaAEsperaDeTeste = true,
				() => DragaoNaoRenovaAEsperaDeTeste = false);

			// =====================================================================
			// 4. "PRA QUEM?" -- as listas e os pedidos com alvo
			// =====================================================================
			GD.Print("[menudragao] -- 4. a segunda etapa: pra mim ou pra outra pessoa --");
			DragaoPara(pl);
			pl.AlvoId = 0;
			EntradaDoDragao? juv = MenuDe(pl).Entrada("juventude");
			string[] rotulos = juv == null ? [] : [.. juv.Escolhas.Select(c => c.Rotulo)];
			Checa("'Juventude' pergunta PRA QUEM: \"Para mim\" primeiro, e quem esta ao alcance do dragao",
				  juv is { Pode: true, TemPergunta: true } && rotulos.Length >= 3 && rotulos[0] == ParaMim && rotulos.Contains(vizinho.Name),
				  string.Join(" | ", rotulos));
			Checa("...e fica de fora quem esta LONGE, quem esta morto e quem morreu de velhice",
				  !rotulos.Contains(longe.Name) && !rotulos.Contains(caido.Name) && !rotulos.Contains(velho.Name), string.Join(" | ", rotulos));
			Checa("...\"Para mim\" pede `juventude`, a outra pessoa pede `juventude_alheia` pelo ID dela (nome se repete)",
				  juv?.Escolhas.Find(c => c.Rotulo == ParaMim)?.Pedido == "juventude"
				  && juv.Escolhas.Find(c => c.Rotulo == vizinho.Name)?.Pedido == $"juventude_alheia #{vizinho.Id}",
				  juv?.Escolhas.Find(c => c.Rotulo == vizinho.Name)?.Pedido ?? "-");

			// O ALVO MARCADO (o duplo clique) vem na frente das outras pessoas, e a janela fica sabendo quando ele muda.
			ServerPlayer outro = Forjar("Bulma", pl.Pos, terra, "bancada_menu_h");
			forjados.Add(outro);
			pl.AlvoId = vizinho.Id;
			List<EscolhaDoDragao> comMarca = MenuDe(pl).Entrada("juventude")?.Escolhas ?? [];
			int iBulma = comMarca.FindIndex(c => c.Rotulo == outro.Name), iKuririn = comMarca.FindIndex(c => c.Rotulo == vizinho.Name);
			Checa("o alvo MARCADO de quem pede vem na frente das outras pessoas (\"Bulma\" viria antes de \"Kuririn\" pelo nome)",
				  iKuririn >= 0 && iBulma > iKuririn && comMarca[iKuririn].Marcado && !comMarca[iBulma].Marcado,
				  string.Join(" | ", comMarca.Select(c => c.Rotulo + (c.Marcado ? "*" : ""))));

			// ---- juventude pra OUTRO
			vizinho.Idade = 60; vizinho.Ficha.Idade = 60;
			pl.Idade = 60; pl.Ficha.Idade = 60;
			int pedidosAntes = set.Pedidos;
			pediu = Pedir(pl, "juventude", vizinho.Name);
			Checa("JUVENTUDE PRA OUTRA PESSOA pelo caminho da janela: ela volta aos 25, e quem pediu continua com 60",
				  pediu && vizinho.Idade == Desejos.IdadeDaJuventude && pl.Idade == 60 && set.Pedidos == pedidosAntes + 1,
				  $"pediu={pediu}, alvo {vizinho.Idade}, eu {pl.Idade}, pedidos {pedidosAntes} -> {set.Pedidos}");

			// ---- juventude PRA MIM
			pediu = Pedir(pl, "juventude", ParaMim);
			Checa("JUVENTUDE \"PARA MIM\": quem pediu volta aos 25", pediu && pl.Idade == Desejos.IdadeDaJuventude, $"pediu={pediu}, eu {pl.Idade}");

			// ---- ressuscitar
			EntradaDoDragao? rev = MenuDe(pl).Entrada("reviver");
			Checa("'Ressuscitar alguém' oferece o morto, NAO oferece quem morreu de velhice, e \"Para mim\" vem apagado (\"você está vivo\")",
				  rev is { Pode: true, TemPergunta: true } && rev.Escolhas.Exists(c => c.Rotulo == caido.Name && c.Pode)
				  && !rev.Escolhas.Exists(c => c.Rotulo == velho.Name)
				  && rev.Escolhas.Find(c => c.Rotulo == ParaMim) is { Pode: false, Nota: "você está vivo" },
				  rev == null ? "sem linha" : string.Join(" | ", rev.Escolhas.Select(c => $"{c.Rotulo}{(c.Pode ? "" : "(apagado)")}")));
			// A RESPOSTA APAGADA NAO SAI DA JANELA -- conferido com o dragao AINDA de pe e a linha acesa, senao a
			// recusa seria a da linha, e nao a da resposta.
			pedidosAntes = set.Pedidos;
			Checa("...e \"Para mim\" apagado nao e pedido: quem esta vivo nao gasta o dragao se ressuscitando",
				  !Pedir(pl, "reviver", ParaMim) && set.Pedidos == pedidosAntes && _invocacoes.ContainsKey(set.Id));
			pediu = Pedir(pl, "reviver", caido.Name);
			Checa("RESSUSCITAR pelo caminho da janela: o morto escolhido volta (e este era o TERCEIRO pedido: o dragao vai embora)",
				  pediu && !caido.Ficha.dead && !_invocacoes.ContainsKey(set.Id), $"pediu={pediu}, morto={caido.Ficha.dead}, dragao de pe={_invocacoes.ContainsKey(set.Id)}");

			// ---- matar: so existe em set de ate dois pedidos (`Wishs<=2`, `WishTable.dm:142`)
			set.Desejos = 2;
			DragaoPara(pl);
			EntradaDoDragao? matar = MenuDe(pl).Entrada("matar");
			Checa("'Matar alguém' (set de 2 pedidos) pergunta QUEM, e nao oferece \"Para mim\" nem quem esta longe",
				  matar is { Pode: true, TemPergunta: true } && matar.Escolhas.Exists(c => c.Rotulo == vizinho.Name)
				  && !matar.Escolhas.Exists(c => c.Rotulo == ParaMim) && !matar.Escolhas.Exists(c => c.Rotulo == longe.Name),
				  matar == null ? "sem linha" : string.Join(" | ", matar.Escolhas.Select(c => c.Rotulo)));
			pediu = Pedir(pl, "matar", vizinho.Name);
			Checa("MATAR pelo caminho da janela: o alvo escolhido (mais fraco que o set) morre", pediu && vizinho.Ficha.dead, $"pediu={pediu}");
			Checa("...e SUMIU da lista de quem se pode matar (e entrou na de quem se pode ressuscitar)",
				  MenuDe(pl).Entrada("matar") is { } m2 && !m2.Escolhas.Exists(c => c.Rotulo == vizinho.Name)
				  && MenuDe(pl).Entrada("reviver") is { } r2 && r2.Escolhas.Exists(c => c.Rotulo == vizinho.Name));
			pediu = Pedir(pl, "reviver", vizinho.Name);
			Checa("...e o segundo pedido o traz de volta", pediu && !vizinho.Ficha.dead);
			// O RESSUSCITADO E PUXADO pra dois tiles de quem pediu (`Ressuscitar`), fora do alcance do dragao: o
			// vizinho das cenas seguintes volta pro lado do testador.
			vizinho.Pos = pl.Pos;
			set.Desejos = 3;

			// ---- curar um planeta: dois mundos mortos de MESMO NOME, dentro do palco de mortes (nada vai pro disco)
			using (PalcoDeMortesDeBancada())
			{
				DragaoPara(pl);
				Checa("sem mundo destruido, 'Curar um planeta' aparece APAGADO e diz por que",
					  MenuDe(pl).Entrada("curar_planeta") is { Pode: false, Motivo: "Nenhum mundo foi destruído." });

				_mortos.Por(new EstadoDaMorte { Chave = "#111", Nome = "Gemeo", Fase = FaseDaMorte.Destruido });
				_mortos.Por(new EstadoDaMorte { Chave = "#222", Nome = "Gemeo", Fase = FaseDaMorte.Destruido });
				EntradaDoDragao? curar = MenuDe(pl).Entrada("curar_planeta");
				Checa("com dois mundos mortos de MESMO NOME a segunda etapa oferece os dois, cada um com a chave dele",
					  curar is { Pode: true, Escolhas.Count: 2 } && curar.Escolhas[0].Pedido != curar.Escolhas[1].Pedido,
					  curar == null ? "sem linha" : string.Join(" | ", curar.Escolhas.Select(c => $"{c.Rotulo}={c.Pedido}")));

				// (pela chave, e nao pelo rotulo como o `Pedir`: os dois se chamam "Gemeo".)
				if (curar?.Escolhas.Find(c => c.Pedido.EndsWith("#222", StringComparison.Ordinal)) is { } segundo)
					Mandar(pl, curar.Verbo, segundo.Pedido);
				Checa("CURAR UM PLANETA pelo caminho da janela: volta o que foi ESCOLHIDO (pela chave), e nao o primeiro com aquele nome",
					  !_mortos.Todos.Any(e => e.Chave == "#222") && _mortos.Todos.Any(e => e.Chave == "#111"),
					  string.Join(",", _mortos.Todos.Select(e => e.Chave)));

				// O gemeo que sobrou sai do registro aqui: o palco de mortes, ao fechar, "devolve o ceu" de todo
				// mundo que morreu dentro dele -- e este nunca teve ceu, so um nome.
				_mortos.Tirar(new ChaveDePlaneta(false, "Gemeo", 111));
			}

			// ---- o corpo de um Saiyajin: so o Kaioshin da classe Golden Apple (`kai_body_wish_ok`)
			ServerPlayer molde = Forjar("Vegeta", new Vec2(pl.Pos.X + 30 * T, pl.Pos.Y), terra, "bancada_menu_i");
			ServerPlayer zamasu = Forjar("Zamasu", pl.Pos, terra, "bancada_menu_j");
			forjados.AddRange([molde, zamasu]);
			molde.Race = "Saiyan"; molde.Ficha.Race = "Saiyan"; molde.Ficha.SaiyanLineage = "Saiyan";
			zamasu.Race = "Kai"; zamasu.Ficha.Race = "Kai";
			zamasu.Class = "Golden Apple"; zamasu.Ficha.Class = "Golden Apple";
			_tronos["kaioshin"] = zamasu.Conta;
			Checa("'Corpo de um Saiyajin' NAO aparece pra quem nao e o Kaioshin", MenuDe(pl).Entrada("corpo_saiyajin") == null);
			DragaoPara(zamasu);
			EntradaDoDragao? corpo = MenuDe(zamasu).Entrada("corpo_saiyajin");
			Checa("...e pro Kaioshin ele pergunta QUAL, com os Saiyajins vivos do mundo inteiro (o molde esta a 30 tiles)",
				  corpo is { Pode: true, TemPergunta: true } && corpo.Escolhas.Exists(c => c.Rotulo == molde.Name),
				  corpo == null ? "sem linha" : string.Join(" | ", corpo.Escolhas.Select(c => c.Rotulo)));
			pediu = Pedir(zamasu, "corpo_saiyajin", molde.Name);
			Checa("O CORPO DE UM SAIYAJIN pelo caminho da janela: ele vira Saiyajin", pediu && zamasu.Race == "Saiyan", $"pediu={pediu}, raca {zamasu.Race}");
			_tronos.Remove("kaioshin");

			// =====================================================================
			// 5. ALVO INVALIDO E RECUSADO -- familia E
			// =====================================================================
			GD.Print("[menudragao] -- 5. o que a janela nao ofereceu nao se pede --");
			bool QuemEstaLongeNaoEAlcancado()
			{
				DragaoPara(pl);
				longe.Idade = 60; longe.Ficha.Idade = 60;
				Verbo(pl, "db_desejar", $"juventude_alheia #{longe.Id}");
				bool recusado = longe.Idade == 60 && set.Pedidos == 0 && _invocacoes.ContainsKey(set.Id);
				longe.Idade = 60; longe.Ficha.Idade = 60;
				return recusado;
			}

			MutacaoDeDesejo(Checa,
				"o id de quem NAO esta na lista (ele esta a 12 tiles) e recusado: ninguem rejuvenesce, nenhum pedido e gasto, o dragao fica",
				"o alvo e aceito pelo id mesmo fora da lista de quem o desejo alcanca",
				QuemEstaLongeNaoEAlcancado,
				() => AlvoForaDaListaDeTeste = true,
				() => AlvoForaDaListaDeTeste = false);

			DragaoPara(pl);
			Verbo(pl, "db_desejar", "reviver #" + vizinho.Id);
			Verbo(pl, "db_desejar", "juventude_alheia #987654");
			Verbo(pl, "db_desejar", "juventude_alheia #" + velho.Id);
			Verbo(pl, "db_desejar", "desejo_que_nao_existe");
			Checa("ressuscitar um VIVO, rejuvenescer um id que nao existe, rejuvenescer um MORTO e um desejo fora da lista: nada e gasto",
				  set.Pedidos == 0 && _invocacoes.ContainsKey(set.Id) && velho.Ficha.dead, $"pedidos {set.Pedidos}");

			double zeniDoVizinho = vizinho.Ficha.Zeni;
			Verbo(vizinho, "db_desejar", "dinheiro");
			Checa("quem NAO invocou nao pede (so quem esta diante do dragao): o zeni dele nao muda e a contagem nao anda",
				  Math.Abs(vizinho.Ficha.Zeni - zeniDoVizinho) < 1 && set.Pedidos == 0);

			// =====================================================================
			// 5b. TODA LINHA DA JANELA, UMA POR UMA
			// =====================================================================
			GD.Print("[menudragao] -- 5b. toda linha da janela, uma por uma --");
			// QUEM PEDE AQUI NAO ERGUEU A ESTATUA (senao "Poder" e o supremo viriam apagados) e tem sangue Saiyajin
			// (senao "Super Saiyajin" nem entra na lista); o set vira o de DOIS pedidos com o supremo comprado. E a
			// lista mais comprida que o dragao comum mostra a alguem.
			ServerPlayer raditz = Forjar("Raditz", pl.Pos, terra, "bancada_menu_m");
			forjados.Add(raditz);
			raditz.Race = "Saiyan"; raditz.Ficha.Race = "Saiyan";
			raditz.Ficha.Genoma = Jandirus.Core.Races.Genome.Pure("Saiyan");
			try
			{
				set.Desejos = 2;
				set.TemSupremo = true;
				if (!caido.Ficha.dead) caido.Combate.Morrer(ignorarSeguro: true);   // "Ressuscitar TODOS" precisa de um morto que volte

				DragaoPara(raditz);
				string[] semPergunta = [.. MenuDe(raditz).Entradas.Where(e => !e.Acao && e.Pode && !e.TemPergunta).Select(e => e.Id)];
				string[] esperadas = ["nada", "dinheiro", "marcos", "tecnologia", "poder", "inteligencia", "super_saiyajin", "reviver_todos", "supremo"];
				Checa("(montagem) pra um Saiyajin que NAO ergueu a estatua, num set de 2 pedidos com o supremo, a janela acende as 9 linhas sem pergunta",
					  semPergunta.ToHashSet().SetEquals(esperadas), string.Join(",", semPergunta));

				var naoConcedeu = new List<string>();
				foreach (string id in semPergunta)
				{
					DragaoPara(raditz);
					bool saiu = Pedir(raditz, id);
					if (!saiu || set.Pedidos != 1 || !_invocacoes.ContainsKey(set.Id))
						naoConcedeu.Add($"{id} (saiu={saiu}, pedidos={set.Pedidos}, dragao de pe={_invocacoes.ContainsKey(set.Id)})");
					raditz.Ficha.sw_doom_year = 0;   // a sentenca do supremo nao fica de pe entre uma linha e a seguinte
				}
				set.Poder = poderDoSet;            // "nada" engorda o set
				Checa($"CADA UMA das {semPergunta.Length} linhas sem pergunta, pedida pela janela, e CONCEDIDA e gasta exatamente UM pedido",
					  semPergunta.Length > 0 && naoConcedeu.Count == 0, string.Join("; ", naoConcedeu));

				// AS APAGADAS: a janela nao as manda; e o cliente mexido, que manda a linha apagada assim mesmo,
				// esbarra no portao de sempre -- nenhum pedido gasto, e o dragao de pe.
				DragaoPara(raditz);
				List<EntradaDoDragao> apagadas = [.. MenuDe(raditz).Entradas.Where(e => !e.Acao && !e.Pode)];
				var cobrou = new List<string>();
				foreach (EntradaDoDragao e in apagadas)
				{
					if (Pedir(raditz, e.Id)) cobrou.Add(e.Id + " (a janela pediu)");
					Verbo(raditz, e.Verbo, e.Pedido);
					if (set.Pedidos != 0 || !_invocacoes.ContainsKey(set.Id)) { cobrou.Add(e.Id); DragaoPara(raditz); }
				}
				Checa($"...e NENHUMA das {apagadas.Count} linhas apagadas e pedida: a janela nao manda, e mandada assim mesmo (cliente mexido) nao gasta pedido",
					  apagadas.Count > 0 && cobrou.Count == 0, string.Join("; ", cobrou));

				raditz.Idade = 40; raditz.Ficha.Idade = 40;
				pediu = Pedir(raditz, "juventude", $"{ParaMim}, extremamente jovem");
				Checa($"JUVENTUDE \"Para mim, extremamente jovem\": {Desejos.IdadeDaJuventudeExtrema} anos",
					  pediu && raditz.Idade == Desejos.IdadeDaJuventudeExtrema, $"pediu={pediu}, idade {raditz.Idade}");

				// A TRAVA DE CONJUNTO: desejo novo na tabela sem prova pelo caminho da janela deixa esta linha vermelha.
				string[] semProva = [.. Desejos.Todos.Where(d => d.Portado && d.Id != "cancelar").Select(d => d.Id).Where(id => !pelaJanela.Contains(id))];
				Checa($"TODOS os {Desejos.Portados} desejos portados da tabela foram pedidos e CONCEDIDOS pelo caminho da janela nesta bancada",
					  semProva.Length == 0, "sem prova: " + string.Join(", ", semProva));
			}
			catch (Exception ex)
			{
				// A EXCECAO DAQUI E UMA FALHA DESTA SECAO, e as seguintes ainda rodam: o que elas medem nao depende dela.
				Checa("a secao 5b correu ate o fim sem lancar excecao", false, ex.ToString());
			}
			finally
			{
				// O MUNDO DAS SECOES SEGUINTES E O DE ANTES: o set de tres pedidos sem o supremo, sem o Saiyajin.
				FecharAInvocacao(set.Id, "");
				_players.Remove(raditz.Id);
				ZoneList(raditz.Zone.Hash).Remove(raditz);
				set.Desejos = 3;
				set.TemSupremo = false;
				set.Poder = poderDoSet;
			}

			// =====================================================================
			// 6. A JANELA SE ACERTA E FECHA -- familias F e G
			// =====================================================================
			GD.Print("[menudragao] -- 6. a janela aberta: o que muda chega, o que nao muda nao, e o dragao indo embora fecha --");
			bool AJanelaFechaComODragao()
			{
				DragaoPara(pl);
				int antes = fio.Count;
				bool dispensou = Pedir(pl, AcoesDoDragao.Dispensar);
				return dispensou && !_invocacoes.ContainsKey(set.Id)
					&& fio.Skip(antes).Any(x => x.Para == pl.Id && x.Menu is { Fechar: true, Dragao: QualDragao.Comum });
			}

			MutacaoDeDesejo(Checa,
				"o dragao DISPENSADO vai embora sem gastar pedido, e a janela de quem o olhava e mandada FECHAR",
				"o dragao vai embora e ninguem avisa a janela",
				AJanelaFechaComODragao,
				() => DragaoSomeCaladoDeTeste = true,
				() => DragaoSomeCaladoDeTeste = false);
			Checa("...e quem teve a janela fechada deixa de ser acertado pelo servidor", !_olhandoODragao.ContainsKey(pl.Id));

			DragaoPara(pl);
			marca = fio.Count;
			_adiantoDoCeu += 130;
			TickDasEsferas();
			Checa("o PRAZO vence (120 s sem pedido): o dragao vai embora sozinho e a janela fecha",
				  !_invocacoes.ContainsKey(set.Id) && Desde(marca, pl).Exists(m => m.Fechar), $"{Desde(marca, pl).Count} pacote(s)");

			// ---- quem so esta olhando
			marca = fio.Count;
			Verbo(vizinho, "db_menu", "");
			MenuDoDragao? previa = Desde(marca, vizinho).LastOrDefault();
			Checa("\"Ver os desejos\" SEM dragao de pe abre a lista do dragao deste mundo, toda apagada, com a frase de por que",
				  previa is { Abrir: true, DePe: false, Dragao: QualDragao.Comum } && previa.Estado.Contains("não está de pé", StringComparison.Ordinal)
				  && previa.Entradas.Where(e => !e.Acao).All(e => !e.Pode) && previa.Entrada(AcoesDoDragao.Dispensar) == null,
				  previa?.Estado ?? "nao chegou menu");
			string[] tabelaDoVizinho = [.. MenuDaqui(vizinho, set).Select(d => d.Id).Where(id => id is not ("cancelar" or "juventude_alheia"))];
			Checa("...e a lista toda apagada sai na ordem da TABELA (nao ha o que por na frente)",
				  previa != null && previa.Entradas.Where(e => !e.Acao).Select(e => e.Id).SequenceEqual(tabelaDoVizinho),
				  previa == null ? "nao chegou menu" : string.Join(",", previa.Entradas.Where(e => !e.Acao).Select(e => e.Id)));

			DragaoPara(pl);
			marca = fio.Count;
			TickDasEsferas();
			MenuDoDragao? deOutro = Desde(marca, vizinho).LastOrDefault();
			Checa("...e quando OUTRO invoca, a janela de quem olhava se acerta sozinha: \"só quem invocou pede\", tudo apagado",
				  deOutro is { DePe: false, Abrir: false } && deOutro.Estado.Contains(pl.Name, StringComparison.Ordinal)
				  && deOutro.Entradas.Where(e => !e.Acao).All(e => !e.Pode), deOutro?.Estado ?? "nao chegou menu");

			// ---- familia G: um pacote quando muda, nenhum quando nao muda
			bool SoQuandoMuda()
			{
				int antes = fio.Count;
				for (int i = 0; i < 4; i++) TickDasEsferas();
				return fio.Count == antes;
			}

			MutacaoDeDesejo(Checa,
				"quatro segundos sem nada mudar: NENHUM menu sai pra quem esta com a janela aberta",
				"o menu sai de novo a cada tique, mudando ou nao",
				SoQuandoMuda,
				() => MenuSaiTodoTiqueDeTeste = true,
				() => MenuSaiTodoTiqueDeTeste = false);

			marca = fio.Count;
			Vec2 ondeEstavaLonge = longe.Pos;
			longe.Pos = pl.Pos;
			TickDasEsferas();
			Checa("alguem CHEGA ao alcance do dragao: a janela de quem invocou recebe UM menu novo, com ele na lista",
				  Desde(marca, pl) is { Count: 1 } chegou && chegou[0].Entrada("juventude") is { } j3 && j3.Escolhas.Exists(c => c.Rotulo == longe.Name),
				  $"{Desde(marca, pl).Count} pacote(s)");
			longe.Pos = ondeEstavaLonge;

			marca = fio.Count;
			pl.AlvoId = outro.Id;
			TickDasEsferas();
			Checa("o jogador MARCA outro alvo com a janela aberta: o menu novo traz esse alvo marcado",
				  Desde(marca, pl).LastOrDefault()?.Entrada("juventude")?.Escolhas.Find(c => c.Marcado)?.Rotulo == outro.Name,
				  $"{Desde(marca, pl).Count} pacote(s)");

			marca = fio.Count;
			Verbo(pl, "db_menu", "fim");
			longe.Pos = pl.Pos;
			TickDasEsferas();
			longe.Pos = ondeEstavaLonge;
			Checa("a janela FECHADA pelo jogador (`db_menu fim`) para de ser acertada -- e o dragao continua de pe",
				  Desde(marca, pl).Count == 0 && _invocacoes.ContainsKey(set.Id), $"{Desde(marca, pl).Count} pacote(s)");

			marca = fio.Count;
			Pedir(pl, AcoesDoDragao.Dispensar);
			Checa("...e quem so OLHAVA o dragao de outro tambem e mandado fechar quando ele vai embora",
				  Desde(marca, vizinho).Exists(m => m.Fechar), $"{Desde(marca, vizinho).Count} pacote(s)");

			// =====================================================================
			// 7. OS BOTOES DA ABA WORLD: abrir numa linha
			// =====================================================================
			GD.Print("[menudragao] -- 7. os gestos da estatua --");
			_olhandoODragao.Clear();
			marca = fio.Count;
			Verbo(pl, "db_menu", "foco " + AcoesDoDragao.Derrubar);
			Checa("\"Derrubar a estátua\" ao lado dela: a janela abre JA na linha de derrubar (foco), em vermelho",
				  Desde(marca, pl).LastOrDefault() is { Abrir: true, Foco: AcoesDoDragao.Derrubar } foco && foco.Entrada(AcoesDoDragao.Derrubar) is { Perigo: true });

			Vec2 aoLadoDaEstatua = pl.Pos;
			pl.Pos = new Vec2(pl.Pos.X + 20 * T, pl.Pos.Y);
			EscutaDeAvisos = [];
			marca = fio.Count;
			Verbo(pl, "db_menu", "foco " + AcoesDoDragao.Derrubar);
			Checa("...e LONGE dela a linha nao vem, e o jogador le por que (a frase do proprio verbo)",
				  Desde(marca, pl).LastOrDefault() is { } semLinha && semLinha.Entrada(AcoesDoDragao.Derrubar) == null
				  && EscutaDeAvisos.Exists(t => t.Contains("ao seu alcance", StringComparison.Ordinal)), string.Join(" | ", EscutaDeAvisos));
			EscutaDeAvisos = null;
			pl.Pos = aoLadoDaEstatua;

			// ---- familia I: os gestos da estatua sao do mundo em que se ESTA
			// Quem invocou e foi pra outro planeta continua vendo (e pedindo a) o dragao dele; mas "Pedidos por
			// invocação" e "Derrubar" agem na estatua do mundo em que o corpo esta, e la o dragao e de outra.
			bool OGestoEDoMundoEmQueSeEsta()
			{
				DragaoPara(pl);
				ZoneKey deAntes = pl.Zone;
				pl.Zone = ZoneKey.Premade("Namek");
				try
				{
					return MenuDe(pl) is { DePe: true, Dragao: QualDragao.Comum } deLonge && deLonge.Entrada("dinheiro") is { Pode: true }
						&& deLonge.Entrada(AcoesDoDragao.Pedidos) == null && deLonge.Entrada(AcoesDoDragao.Derrubar) == null;
				}
				finally { pl.Zone = deAntes; }
			}

			MutacaoDeDesejo(Checa,
				"quem invocou e foi pra OUTRO mundo continua podendo pedir, mas nao ve \"Pedidos por invocação\" nem \"Derrubar\": sao gestos da estatua do mundo em que se esta",
				"o gesto aparece pra quem ergueu a estatua do dragao que esta olhando, esteja no mundo que estiver",
				OGestoEDoMundoEmQueSeEsta,
				() => GestoDeOutroMundoDeTeste = true,
				() => GestoDeOutroMundoDeTeste = false);
			Checa("...e de volta ao mundo da estatua (e ao lado dela) os dois gestos estao la",
				  MenuDe(pl) is { } deVolta && deVolta.Entrada(AcoesDoDragao.Pedidos) is { Pode: true } && deVolta.Entrada(AcoesDoDragao.Derrubar) is { Pode: true });
			FecharAInvocacao(set.Id, "");
			_olhandoODragao.Clear();

			pediu = Pedir(pl, AcoesDoDragao.Pedidos, "2 pedidos");
			Checa("\"Pedidos por invocação\" pela janela (quem ergueu escolhe 2): a estatua passa a dar 2, e as sete voltam a dormir",
				  pediu && set.Desejos == 2 && !SetAtivo(set), $"pediu={pediu}, desejos {set.Desejos}");
			Checa("...e o gesto NAO aparece pra quem nao ergueu a estatua", MenuDe(vizinho).Entrada(AcoesDoDragao.Pedidos) == null);

			pediu = Pedir(pl, AcoesDoDragao.Derrubar);
			Checa("\"Derrubar a estátua\" pela janela: o set deste mundo deixa de existir",
				  pediu && !_sets.Contains(set) && !_esferas.Exists(e => e.Set == set.Id), $"pediu={pediu}");
			Checa("...e a janela passa a dizer que nao ha dragao neste mundo",
				  MenuDe(pl) is { Dragao: QualDragao.Nenhum, DePe: false } nenhum && nenhum.Estado.Contains("Não há Estátua", StringComparison.Ordinal));

			// =====================================================================
			// 8. O SUPER SHENRON PELO MESMO CAMINHO
			// =====================================================================
			GD.Print("[menudragao] -- 8. o Super Shenron --");
			// (palco) O ELENCO DAQUI ESTA NA TERRA, e o dragao so e chamado ao lado de uma Super Esfera (`set src in
			// oview(2)`, `ProceduralSpace.dm:1580`) -- a janela apaga os desejos de quem esta longe delas. Quem mede
			// o LUGAR (o verbo e a janela, num planeta e ao lado de uma esfera) e a `--validacoesteste`; esta secao
			// mede as cinco situacoes do menu com o portao de lugar aberto, e ele volta a fechar no `finally`.
			SuperShenronDeQualquerLugarDeTeste = true;
			foreach (SuperEsfera s in _supers) { s.Dono = ""; s.DonoNome = ""; }
			_benefSig = _benefNome = _pedidoDoBenef = _alvoDoPedido = "";
			_ofertasDeGuarda.Clear();
			_precoPendente = default;
			_disputasDeSuper.Clear();
			_olhandoODragao.Clear();

			ServerPlayer falante = Forjar("Kaio", pl.Pos, terra, "bancada_menu_k");
			ServerPlayer mudo = Forjar("Mudo", pl.Pos, terra, "bancada_menu_l");
			forjados.AddRange([falante, mudo]);
			falante.Race = "Kai"; falante.Ficha.Race = "Kai";
			ConferirALingua(falante);
			mudo.Race = "Human"; mudo.Ficha.ParentRace = ""; mudo.Ficha.godtongue = false;

			DarSupers(mudo, 3);
			Checa("com 3 de 7 a janela do Super Shenron mostra a lista apagada e quantas faltam",
				  MenuDe(mudo, QualDragao.Super) is { Dragao: QualDragao.Super, DePe: false } tres && tres.Estado.Contains("3 de 7", StringComparison.Ordinal)
				  && tres.Entradas.Count > 0 && tres.Entradas.All(e => !e.Pode));

			DarSupers(mudo, SuperEsferas.Total);
			MenuDoDragao semLingua = MenuDe(mudo, QualDragao.Super);
			EntradaDoDragao? emprestar = semLingua.Entrada(AcoesDoDragao.Emprestar);
			Checa("com as sete e SEM a lingua: os desejos apagados, a frase que ensina a lingua, e o gesto de EMPRESTAR aceso",
				  !semLingua.DePe && semLingua.Estado.Contains("LÍNGUA DOS DEUSES", StringComparison.Ordinal)
				  && semLingua.Entradas.Where(e => !e.Acao).All(e => !e.Pode) && emprestar is { Pode: true, TemPergunta: true });
			Checa("...que lista quem esta AO LADO, e diz de cada um se fala a lingua",
				  emprestar?.Escolhas.Find(c => c.Rotulo == falante.Name) is { Pode: true } f1 && f1.Nota.StartsWith("fala", StringComparison.Ordinal)
				  && emprestar.Escolhas.Find(c => c.Rotulo == outro.Name) is { } f2 && f2.Nota.StartsWith("NÃO fala", StringComparison.Ordinal)
				  && !emprestar.Escolhas.Exists(c => c.Rotulo == longe.Name),
				  emprestar == null ? "sem gesto" : string.Join(" | ", emprestar.Escolhas.Select(c => $"{c.Rotulo}: {c.Nota}")));

			Verbo(mudo, "sdb_transferir", "#" + longe.Id);
			Checa("emprestar a quem NAO esta ao lado (pelo id) e recusado: nenhuma oferta nasce", _ofertasDeGuarda.Count == 0);

			marca = fio.Count;
			pediu = Pedir(mudo, AcoesDoDragao.Emprestar, falante.Name, QualDragao.Super);
			Checa("EMPRESTAR AS SETE pela janela: a oferta nasce, e a janela de quem a recebe ABRE na linha de aceitar (o `alert` do DM)",
				  pediu && _ofertasDeGuarda.ContainsKey(falante.Conta)
				  && Desde(marca, falante).LastOrDefault() is { Abrir: true, Foco: AcoesDoDragao.AceitarGuarda, Dragao: QualDragao.Super } oferta
				  && oferta.Entrada(AcoesDoDragao.AceitarGuarda) is { Pode: true } && oferta.Entrada(AcoesDoDragao.RecusarGuarda) is { Direto: true },
				  $"pediu={pediu}, ofertas {_ofertasDeGuarda.Count}, pacotes {Desde(marca, falante).Count}");

			marca = fio.Count;
			pediu = Pedir(falante, AcoesDoDragao.AceitarGuarda, null, QualDragao.Super);
			Checa("ACEITAR A GUARDA pela janela: as sete passam, e a janela de quem emprestou ABRE pra ele escolher o pedido",
				  pediu && QuantasSupersTem(falante.Assinatura) == SuperEsferas.Total && string.Equals(_benefSig, mudo.Assinatura, StringComparison.Ordinal)
				  && Desde(marca, mudo).Exists(m => m is { Abrir: true, DePe: true } && m.Entrada("sdb_riqueza") is { Pode: true, Verbo: "sdb_pedido" } && m.Entrada(AcoesDoDragao.Retomar) != null),
				  $"pediu={pediu}, sete com o falante={QuantasSupersTem(falante.Assinatura)}");
			Checa("...e o porta-voz ve UMA linha, PRONUNCIAR, apagada enquanto o pedido nao foi dito",
				  MenuDe(falante, QualDragao.Super) is { DePe: false } esperando && esperando.Entradas.Count(e => !e.Acao) == 0
				  && esperando.Entrada(AcoesDoDragao.Pronunciar) is { Pode: false } pr && pr.Motivo.Contains("ainda não disse", StringComparison.Ordinal));

			pediu = Pedir(mudo, "sdb_riqueza", null, QualDragao.Super);
			Checa("REGISTRAR O PEDIDO pela janela (quem emprestou escolhe): fica gravado, e a linha passa a dizer que e o registrado",
				  pediu && _pedidoDoBenef == "sdb_riqueza" && MenuDe(mudo, QualDragao.Super).Entrada("sdb_riqueza") is { Pode: true } reg
				  && reg.Motivo.Contains("registrado", StringComparison.Ordinal), $"pediu={pediu}, registrado '{_pedidoDoBenef}'");
			Checa("...e o PRONUNCIAR do porta-voz acende, com o nome do pedido",
				  MenuDe(falante, QualDragao.Super).Entrada(AcoesDoDragao.Pronunciar) is { Pode: true, Verbo: "sdb_invocar", Pedido: "" } dito
				  && dito.Nome.Contains("Riqueza", StringComparison.Ordinal));

			double zeniDoMudo = mudo.Ficha.Zeni, zeniDoFalante = falante.Ficha.Zeni;
			int cicloAntes = _cicloDasSupers;
			marca = fio.Count;
			pediu = Pedir(falante, AcoesDoDragao.Pronunciar, null, QualDragao.Super);
			Checa("PRONUNCIAR pela janela: a riqueza cai em quem EMPRESTOU, e nao em quem falou, e as sete se espalham",
				  pediu && Math.Abs(mudo.Ficha.Zeni - (zeniDoMudo + Desejos.ZeniDoSuperDesejo)) < 1
				  && Math.Abs(falante.Ficha.Zeni - zeniDoFalante) < 1 && _cicloDasSupers == cicloAntes + 1,
				  $"pediu={pediu}, mudo {zeniDoMudo:N0}->{mudo.Ficha.Zeni:N0}, ciclo {cicloAntes}->{_cicloDasSupers}");
			Checa("...e as duas janelas (a de quem falou e a de quem emprestou) sao mandadas FECHAR: as sete se foram",
				  Desde(marca, falante).Exists(m => m is { Fechar: true, Dragao: QualDragao.Super })
				  && Desde(marca, mudo).Exists(m => m is { Fechar: true, Dragao: QualDragao.Super }));

			// ---- quem tem as sete e fala: pede direto, com alvo
			if (!caido.Ficha.dead) caido.Combate.Morrer(ignorarSeguro: true);
			DarSupers(falante, SuperEsferas.Total);
			marca = fio.Count;
			Verbo(falante, "sdb_invocar", "");
			MenuDoDragao? doFalante = Desde(marca, falante).LastOrDefault();
			Checa("\"Chamar o Super Shenron\" com as sete e a lingua ABRE a janela (era a lista no chat), com os desejos acesos",
				  doFalante is { Abrir: true, DePe: true, Dragao: QualDragao.Super } && doFalante.Entrada("sdb_riqueza") is { Pode: true, Verbo: "sdb_invocar" }
				  && doFalante.Entrada("sdb_reviver") is { Pode: true, TemPergunta: true } && doFalante.Entrada("sdb_supremo") is { Perigo: true });

			cicloAntes = _cicloDasSupers;
			Verbo(falante, "sdb_invocar", "sdb_reviver #" + outro.Id);
			Checa("ressuscitar um VIVO pelo Super Shenron (id fora da lista) e recusado: o ciclo nao anda, as sete ficam",
				  _cicloDasSupers == cicloAntes && QuantasSupersTem(falante.Assinatura) == SuperEsferas.Total);
			pediu = Pedir(falante, "sdb_reviver", caido.Name, QualDragao.Super);
			Checa("REVIVER pelo Super Shenron, com o morto escolhido na janela: ele volta e as sete se consomem",
				  pediu && !caido.Ficha.dead && _cicloDasSupers == cicloAntes + 1, $"pediu={pediu}, morto={caido.Ficha.dead}");

			// ---- o preco do desejo supremo: a palavra virou um botao (era uma frase pra digitar)
			DarSupers(falante, SuperEsferas.Total);
			falante.Ficha.sw_doom_year = 0;
			cicloAntes = _cicloDasSupers;
			marca = fio.Count;
			pediu = Pedir(falante, "sdb_supremo", null, QualDragao.Super);
			Checa("o desejo SUPREMO pedido pela janela NAO acontece na hora: a janela de quem paga ABRE na pergunta do preco",
				  pediu && falante.Ficha.sw_doom_year <= 0 && _cicloDasSupers == cicloAntes
				  && Desde(marca, falante).LastOrDefault(m => m.Abrir) is { Foco: AcoesDoDragao.Preco } preco
				  && preco.Entrada(AcoesDoDragao.Preco) is { Perigo: true, Verbo: "sdb_aceito" } && preco.Entrada(AcoesDoDragao.RecusarPreco) is { Direto: true },
				  $"pediu={pediu}, divida {falante.Ficha.sw_doom_year:0}");

			pediu = Pedir(falante, AcoesDoDragao.RecusarPreco, null, QualDragao.Super);
			Checa("\"Recusar o preço\": nada acontece, as sete ficam, e a pergunta some da janela",
				  pediu && falante.Ficha.sw_doom_year <= 0 && _cicloDasSupers == cicloAntes
				  && QuantasSupersTem(falante.Assinatura) == SuperEsferas.Total && MenuDe(falante, QualDragao.Super).Entrada(AcoesDoDragao.Preco) == null);

			Pedir(falante, "sdb_supremo", null, QualDragao.Super);
			pediu = Pedir(falante, AcoesDoDragao.Preco, null, QualDragao.Super);
			Checa("\"ACEITO O PREÇO\" pela janela: a divida nasce e as sete se consomem",
				  pediu && falante.Ficha.sw_doom_year > 0 && _cicloDasSupers == cicloAntes + 1,
				  $"pediu={pediu}, divida {falante.Ficha.sw_doom_year:0}, ciclo {cicloAntes}->{_cicloDasSupers}");
			falante.Ficha.sw_doom_year = 0;   // a bancada nao deixa uma sentenca de morte de pe

			// ---- quem tem assunto com o Super Shenron e esta num planeta acha a porta dele no menu que abriu
			Checa("sem assunto com o Super Shenron, o menu de um planeta NAO traz o atalho dele", MenuDe(pl).Entrada(AcoesDoDragao.Super) == null);
			DarSupers(pl, SuperEsferas.Total);
			marca = fio.Count;
			pediu = Pedir(pl, AcoesDoDragao.Super);
			Checa("...e com as sete na mao ele traz, e o atalho troca a janela pro Super Shenron na hora (sem confirmar)",
				  pediu && Desde(marca, pl).LastOrDefault() is { Abrir: true, Dragao: QualDragao.Super }, $"pediu={pediu}");
			DarSupers(pl, 0);

			// =====================================================================
			// 9. O FIO
			// =====================================================================
			GD.Print("[menudragao] -- 9. o fio --");
			static bool Fiel((int Para, MenuDoDragao Menu, byte[] Fio) x)
			{
				var r = new LiteNetLib.Utils.NetDataReader(x.Fio);
				if ((Protocol.S2C)r.GetByte() != Protocol.S2C.MenuDoDragao) return false;
				MenuDoDragao lido = FioDoMenuDoDragao.Ler(r);
				if (r.AvailableBytes != 0 || lido.Fechar != x.Menu.Fechar || lido.Dragao != x.Menu.Dragao) return false;
				return x.Menu.Fechar || (lido.Assinatura() == x.Menu.Assinatura() && lido.Abrir == x.Menu.Abrir
										 && lido.Foco == x.Menu.Foco && lido.Segundos == x.Menu.Segundos);
			}

			int infieis = fio.Count(x => !Fiel(x));
			Checa($"os {fio.Count} pacotes que sairam se leem INTEIROS e dao o mesmo menu (nenhum texto cortado, nenhum byte sobrando)",
				  fio.Count > 30 && infieis == 0, $"{infieis} infiel(is) de {fio.Count}");
			Checa("...e houve dos dois tipos: menus e avisos de fechar",
				  fio.Exists(x => x.Menu.Fechar) && fio.Exists(x => !x.Menu.Fechar && x.Menu.Entradas.Count > 10));

			// O CONTRA-EXEMPLO DO FIO: um byte a menos no fim e o leitor nao pode devolver o mesmo menu.
			(int Para, MenuDoDragao Menu, byte[] Fio) cheio = fio.First(x => !x.Menu.Fechar && x.Menu.Entradas.Count > 10);
			bool cortadoReprova;
			try { cortadoReprova = !Fiel((cheio.Para, cheio.Menu, cheio.Fio[..^3])); }
			catch (Exception) { cortadoReprova = true; }
			Checa("(defeito injetado: tres bytes a menos no fim do pacote) a mesma conferencia REPROVA", cortadoReprova);
		}
		catch (Exception ex)
		{
			// UMA EXCECAO E UMA FALHA, E NAO UM FIM: esta bancada roda dentro do tratador de pacote da rede, que
			// engole excecao num aviso -- sem este bloco ela parava no meio e imprimia um placar verde.
			falhou++;
			GD.PrintErr($"[menudragao]   FALHA (EXCECAO -- a bancada parou aqui): {ex}");
		}
		finally
		{
			DragaoSobeSemJanelaDeTeste = MenuNaoApagaNadaDeTeste = DragaoSomeCaladoDeTeste = MenuSaiTodoTiqueDeTeste = false;
			AlvoForaDaListaDeTeste = ListaNaOrdemDaTabelaDeTeste = GestoDeOutroMundoDeTeste = false;
			ContagemZeraTodoTiqueDeTeste = DragaoNaoRenovaAEsperaDeTeste = false;
			SuperShenronDeQualquerLugarDeTeste = false;   // o palco da secao 8

			foreach (ServerPlayer f in forjados)
			{
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
			}

			_sets.Clear();
			_sets.AddRange(setsGuardados);
			_esferas.Clear();
			_esferas.AddRange(esferasGuardadas);
			_invocacoes.Clear();
			_olhandoODragao.Clear();

			foreach ((int n, string dono, string nome) in supersGuardadas)
				if (_supers.Find(s => s.Numero == n) is { } s) { s.Dono = dono; s.DonoNome = nome; }
			_cicloDasSupers = cicloGuardado;
			(_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido) = benefGuardado;
			_disputasDeSuper.Clear();
			_ofertasDeGuarda.Clear();
			_precoPendente = default;

			_adiantoDoCeu = ceuGuardado;

			pl.Race = racaGuardada;
			pl.Class = classeGuardada;
			pl.Ficha.BP = bpGuardado;
			pl.Ficha.Zeni = zeniGuardado;
			pl.Idade = idadeGuardada;
			pl.Ficha.Idade = idadeGuardada;
			pl.Ficha.godtongue = linguaGuardada;
			pl.Ficha.wishedpoints = marcosGuardados;
			pl.Ficha.sw_doom_year = 0;
			pl.AlvoId = alvoGuardado;
			pl.Ficha.Statify();
			MoveToZone(pl.Id, zonaGuardada, posGuardada);

			_tronos.Clear();
			foreach ((string k, string v) in tronosGuardados) _tronos[k] = v;

			SalvarEsferas();
			SalvarSupers();
			Persistir(pl);

			EscutaDosMenusDoDragao = null;
			EscutaDosDesejos = null;
			EscutaDasSupers = null;
			EscutaDeAvisos = escutaDeAvisosAntes;

			GD.Print($"[menudragao] ================ {ok} OK, {falhou} FALHA(S) ================\n");
		}
	}

	// =====================================================================
	// O PALCO DA `--diagdesejos` -- o que o robo da janela pede ao servidor do mesmo processo
	// =====================================================================
	/// <summary>Os corpos forjados do palco da <c>--diagdesejos</c>, pra desfazer no fim.</summary>
	private readonly List<ServerPlayer> _genteDoPalcoDosDesejos = [];

	/// <summary>
	/// MONTA O PALCO DA JANELA DE DESEJOS em volta de um jogador (a bancada `--diagdesejos`, modo host): um set
	/// de TRES pedidos neste mundo, erguido por ele e acordado, as sete em roda, e cinco pessoas -- duas ao
	/// lado (a primeira e o alvo MARCADO dele, e tem 60 anos), uma longe, um morto e um morto de velhice.
	///
	/// A ESTATUA NAO PASSA PELA PORTA DE PRODUCAO (`ErguerEstatua`), pelo motivo do `EsferasNoTrailer`: ela
	/// exige um Namekuseijin do Cla do Dragao que seja o Guardiao, e o que a janela mede comeca depois disso.
	/// Tudo o que vem depois -- invocar, pedir, dispensar -- e o verbo do jogador.
	/// </summary>
	/// <returns>Os ids das cinco pessoas, na ordem do texto acima; zeros se nao deu pra montar.</returns>
	internal (int Marcado, int AoLado, int Longe, int Morto, int Velho) PalcoDosDesejosDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || CorpoDaZona(pl.Zone) == null) return default;
		DesfazerOPalcoDosDesejosDeTeste();

		SetDeEsferas? s = _sets.Find(x => x.Zona.Hash == pl.Zone.Hash);
		if (s == null)
		{
			int novo = ProximoIdDeSet();
			if (novo < 0) return default;
			s = new SetDeEsferas
			{
				Id = novo,
				// Ao lado do jogador (ao alcance da mao, pra "Derrubar a estatua"), fora do tile do dragao.
				Ex = pl.Pos.X - 1.25f * ZoneCollision.TileSize,
				Ey = pl.Pos.Y + 0.75f * ZoneCollision.TileSize,
				CriadorSig = pl.Assinatura,
				CriadorConta = pl.Conta,
				CriadorNome = pl.Name,
				Poder = 5_000_000,
				Desejos = 3,
				OffTime = Esferas.OffTimeDeJogador,
			};
			s.PorZona(pl.Zone);
			_sets.Add(s);
			RefazerAsEsferas(s);
		}

		const int t = ZoneCollision.TileSize;
		ServerPlayer Gente(string nome, float dx, float dy, string conta)
		{
			ServerPlayer p = Forjar(nome, new Vec2(pl.Pos.X + dx * t, pl.Pos.Y + dy * t), pl.Zone, conta);
			// A CARA E A DO JOGADOR: um corpo forjado nasce sem aparencia, e a foto mostraria um vulto.
			p.Visual = pl.Visual.Copiar();
			TrocarAparencias(p);
			_genteDoPalcoDosDesejos.Add(p);
			return p;
		}

		ServerPlayer marcado = Gente("Kuririn", 1.2f, 0.4f, "bancada_palco_b");
		ServerPlayer aoLado = Gente("Bulma", -1.1f, -0.3f, "bancada_palco_c");
		ServerPlayer longe = Gente("Yamcha", 9, 3, "bancada_palco_d");
		ServerPlayer morto = Gente("Tenshinhan", 4, 4, "bancada_palco_e");
		ServerPlayer velho = Gente("Mestre Kame", 5, 4, "bancada_palco_f");
		marcado.Idade = 60; marcado.Ficha.Idade = 60;
		morto.Combate.Morrer(ignorarSeguro: true);
		velho.Combate.Morrer(ignorarSeguro: true);
		velho.Ficha.aged_out = true;
		pl.AlvoId = marcado.Id;

		ReporODragaoDeTeste(id);
		return (marcado.Id, aoLado.Id, longe.Id, morto.Id, velho.Id);
	}

	/// <summary>
	/// O SET DO PALCO VOLTA A ESTAR PRONTO PRA INVOCAR: acordado, com a contagem em zero, sem dragao de pe e com
	/// as sete em roda do jogador. E o que separa duas cenas da `--diagdesejos` -- a espera de um ano entre
	/// duas invocacoes e regra de producao, medida pela `--esferateste`, e nao cabe numa bancada de janela.
	/// </summary>
	internal bool ReporODragaoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (_sets.Find(x => x.Zona.Hash == pl.Zone.Hash) is not { } s) return false;

		FecharAInvocacao(s.Id, "");
		s.Inerte = false;
		s.AtivoEm = 0;
		s.Pedidos = 0;

		// A RODA: as sete dentro do alcance da invocacao (`Esferas.AlcanceDaInvocacao`, medido por eixo).
		int k = 0;
		foreach (Esfera e in _esferas.Where(x => x.Set == s.Id).OrderBy(x => x.Numero))
		{
			double ang = k++ * (Math.PI * 2 / Esferas.Total) - Math.PI / 2;
			e.Portador = 0;
			e.PorZona(pl.Zone);
			e.X = pl.Pos.X + (float)(Math.Cos(ang) * 0.9 * Esferas.AlcanceDaInvocacao);
			e.Y = pl.Pos.Y + (float)(Math.Sin(ang) * 0.6 * Esferas.AlcanceDaInvocacao);
			e.PrecisaAssentar = false;
		}

		MandarEsferas(pl.Zone);
		TickDasEsferas();   // o tique de producao ve o set acordado, como em jogo
		return true;
	}

	/// <summary>O set do palco como o servidor o tem: pedidos gastos, pedidos por invocacao, e se ha dragao de pe.</summary>
	internal (int Pedidos, int Desejos, bool DragaoDePe, bool Existe) SetDoPalcoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return default;
		return _sets.Find(x => x.Zona.Hash == pl.Zone.Hash) is { } s
			? (s.Pedidos, s.Desejos, _invocacoes.ContainsKey(s.Id), true)
			: default;
	}

	/// <summary>Um corpo como o servidor o tem: o zeni, a idade e se esta morto. Nulo se nao existe.</summary>
	internal (double Zeni, int Idade, bool Morto)? CorpoDosDesejosDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? p) ? (p.Ficha.Zeni, p.Idade, p.Ficha.dead) : null;

	/// <summary>
	/// AS SETE SUPER ESFERAS NA MAO DO JOGADOR, e a Lingua dos Deuses na boca dele: o ponto de partida da cena do
	/// Super Shenron na `--diagdesejos`. Reivindicar e aprender a lingua sao medidos pela `--avessoteste`.
	/// </summary>
	internal int SupersNaMaoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return -1;
		pl.Ficha.godtongue = true;
		DarSupers(pl, SuperEsferas.Total);
		MandarSupers(pl);
		return _cicloDasSupers;
	}

	/// <summary>
	/// O ciclo das Super Esferas AGORA: anda um quando as sete se consomem num desejo. (Quem ESCREVE o ciclo e
	/// o `CicloDasSupersDeTeste(int)` da bancada dos verbs -- por isso o nome diferente.)
	/// </summary>
	internal int CicloDasSupersAgoraDeTeste => _cicloDasSupers;

	/// <summary>
	/// AS PESSOAS DO PALCO SAEM. O set e as Super Esferas ficam como a cena os deixou: a pasta de saves de uma
	/// bancada e descartavel (o `bancada.py` a apaga a cada corrida) e o processo morre logo depois do placar.
	/// </summary>
	internal void DesfazerOPalcoDosDesejosDeTeste()
	{
		foreach (ServerPlayer p in _genteDoPalcoDosDesejos) RemoverNpc(p);
		_genteDoPalcoDosDesejos.Clear();
	}
}
