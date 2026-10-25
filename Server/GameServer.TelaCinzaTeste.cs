using Godot;
using Jandirus.Core.Races;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--telacinzateste` -- A TELA CINZA DA CRIACAO, do lado do servidor (2026-10-23). O relato do dono: *"alguns jogadores caem
/// em uma tela cinza na criaçao"*. A tela em si (a recusa que reabre a criacao com o motivo) e do robo `--diagtelacinza`; aqui ficam as
/// duas portas do servidor, com os jogadores entrando pela porta (clientes crus):
///
///   TC1. O SORTEIO VELHO DA CONTA: a vaga sorteada como "Icer" (a chave do Frost Demon antes da v0.4.3) volta do disco como
///        "Frost Demon" -- e as outras vagas como estavam. CONTRA-EXEMPLO `AccountStore.SorteioVelhoFicaDeTeste`.
///   TC2. ...E NASCE: no modo de racas aleatorias, a lista de vagas diz Frost Demon e a ficha que a tela manda (a raca da vaga e o mundo
///        dela) entra no mundo. Com o defeito, a lista diz "Icer" e a mesma criacao e recusada ("raca nao pertence a esse planeta") --
///        a tela cinza de quem tinha conta de antes da v0.4.3.
///   TC3. A EXCECAO NA CRIACAO RESPONDE: a criacao que estoura (`CriacaoEstouraDeTeste`, a reproducao forcada) e recusada com o motivo
///        generico, o erro inteiro vai pro log, a lista de vagas volta junto (a vaga continua vazia) e o mesmo jogador cria de novo.
///        CONTRA-EXEMPLO `ErroDaCriacaoCaladoDeTeste` (o jogo de antes: so o aviso no log, o cliente esperando pra sempre).
///   TC4. ...MAS NAO A QUEM JA ENTROU: a excecao com o corpo no mundo nao manda recusa (ela reabriria a criacao por cima do mundo).
///        CONTRA-EXEMPLO `RecusaDepoisDeEntrarDeTeste`.
///   TC5. (2026-10-24, o dono: "Consertar") O "JOGAR" TAMBEM RESPONDE: o `PickSlot` de quem ja tem personagem que estoura
///        (`JogarEstouraDeTeste`) e recusado com o motivo generico, o erro vai pro log e o mesmo jogador entra de novo; quem ja entrou
///        nao e recusado. CONTRA-EXEMPLOS `ErroDoJogarCaladoDeTeste` e `RecusaDepoisDeEntrarDeTeste`.
///
///     Godot --headless --path . --server --port 10812 --telacinzateste
/// Placar: `[telacinza] ================ N OK, M FALHA(S) ================`.
/// </summary>
public partial class GameServer
{
	private bool _tciRodando, _tciJaRodou;
	private int _tciOk, _tciFalhou;

	private static readonly string[] ContasDaTelaCinza =
	[
		"bancada_tc_icer", "bancada_tc_icer_velho", "bancada_tc_erro", "bancada_tc_calado", "bancada_tc_dentro", "bancada_tc_dentro_b",
		// (2026-10-24) as do "Jogar" (TC5)
		"bancada_tc_jogar", "bancada_tc_jogar_calado", "bancada_tc_jogar_dentro", "bancada_tc_jg_dentro_b",
	];

	private void AfirmarTci(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _tciOk++; GD.Print($"[telacinza]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _tciFalhou++; GD.PrintErr($"[telacinza]   FALHA {nome}   [{detalhe}]"); }
	}

	private void ApagarContasDaTelaCinza()
	{
		if (_store == null) return;
		foreach (string conta in ContasDaTelaCinza)
			try
			{
				string caminho = System.IO.Path.Combine(_store.Pasta, AccountStore.NomeDeArquivo(conta) + ".json");
				if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[telacinza] nao apaguei '{conta}': {e.Message}"); }
	}

	/// <summary>A conta como a v0.4.2 a gravava: senha "teste" (a dos clientes crus) e a vaga 1 sorteada com a chave velha "Icer".</summary>
	private void GravarContaComOSorteioVelho(string conta)
	{
		(string sal, string hash) = AccountStore.Cadastrar("teste");
		var velha = new AccountSave { Conta = conta, Sal = sal, Hash = hash, CriadaEm = NowMs() };
		velha.Sorteios[0] = new RacaSorteada { Raca = FormasDeFrost.RacaLegada };
		velha.Sorteios[2] = new RacaSorteada { Raca = "Saiyan", Linhagem = "Primal Saiyan" };
		velha.SorteiosFeitos = 2;
		_store!.Gravar(velha);
	}

	/// <summary>A ficha que a CRIACAO manda pra uma vaga sorteada: a raca e a linhagem da vaga, o mundo da raca (`CreationScreen.Reabrir`).</summary>
	private static CharacterDraft FichaDaVagaSorteada(string nome, SlotInfo vaga)
	{
		string raca = vaga.RacaSorteada ?? "";
		var ficha = new CharacterDraft
		{
			Name = nome, Race = raca, Planet = SorteioDeRaca.PlanetaDaRaca(raca), ChosenClass = vaga.LinhagemSorteada ?? "",
			Gender = "Male", Backstory = "Personagem da bancada da tela cinza, nascido de uma vaga sorteada.",
		};
		ficha.Age = Math.Min(18, Math.Max(1, ficha.IdadeMaxima));
		return ficha;
	}

	public void RodarBancadaDaTelaCinza(int porta)
	{
		if (_tciRodando || _tciJaRodou) return;
		_tciRodando = _tciJaRodou = true;
		_tciOk = _tciFalhou = 0;
		GD.Print("[telacinza] ================ A TELA CINZA DA CRIACAO, O LADO DO SERVIDOR (dono, 2026-10-23) ================");

		ApagarContasDaTelaCinza();
		var c = new ClienteCruDeTeste[ContasDaTelaCinza.Length];
		for (int i = 0; i < c.Length; i++) c[i] = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, c);
		Jandirus.Core.Races.ModoDeRacas modoAntes = ModoDeRacas;
		try
		{
			if (_store == null) { AfirmarTci("(montagem) o servidor tem onde gravar as contas", false); return; }
			string Arquivo(string conta) => System.IO.Path.Combine(_store.Pasta, AccountStore.NomeDeArquivo(conta) + ".json");

			// ============================================================ TC1) o sorteio velho da conta
			GD.Print("[telacinza] -- TC1) o sorteio velho \"Icer\" da conta --");
			GravarContaComOSorteioVelho(ContasDaTelaCinza[0]);
			bool noDiscoVelho = System.IO.File.ReadAllText(Arquivo(ContasDaTelaCinza[0])).Contains($"\"{FormasDeFrost.RacaLegada}\"");
			static bool SorteioDeAgora(AccountSave? a) =>
				a?.Sorteios[0]?.Raca == FormasDeFrost.Raca && a.Sorteios[1] == null
				&& a.Sorteios[2] is { Raca: "Saiyan", Linhagem: "Primal Saiyan" };
			static string Vagas(AccountSave? a) => a == null ? "conta nao lida"
				: string.Join(" | ", a.Sorteios.Select(s => s == null ? "-" : $"{s.Raca}/{s.Linhagem}"));
			AccountSave? lida = _store.Carregar(ContasDaTelaCinza[0]);
			AfirmarTci("TC1a. a conta gravada com o sorteio velho \"Icer\" (a chave do Frost Demon antes da v0.4.3) volta do disco com \"Frost Demon\" -- e as outras vagas como estavam",
				noDiscoVelho && SorteioDeAgora(lida), $"no disco 'Icer': {noDiscoVelho}; lida: {Vagas(lida)}");
			AccountSave? comDefeito;
			AccountStore.SorteioVelhoFicaDeTeste = true;
			try { comDefeito = _store.Carregar(ContasDaTelaCinza[0]); }
			finally { AccountStore.SorteioVelhoFicaDeTeste = false; }
			AfirmarTci("TC1b. CONTRA-EXEMPLO (defeito injetado: a carga deixa o sorteio velho como estava, o jogo de antes): a mesma conta volta com \"Icer\" -- a regua REPROVA",
				!SorteioDeAgora(comDefeito), Vagas(comDefeito));

			// ============================================================ TC2) ...e nasce, pela porta
			GD.Print("[telacinza] -- TC2) a vaga do sorteio velho nasce (modo de racas aleatorias, pela porta) --");
			ModoDeRacas = Jandirus.Core.Races.ModoDeRacas.Aleatorio;
			bool logou = mesa.Entra(c[0]) && mesa.Loga(c[0], ContasDaTelaCinza[0]) && c[0].Slots is { Count: > 0 };
			SlotInfo vaga = logou ? c[0].Slots![0] : default;
			AfirmarTci("TC2a. no modo de racas aleatorias a lista de vagas diz Frost Demon pra vaga sorteada como \"Icer\" -- e a criacao abre com ele",
				logou && vaga.RacaSorteada == FormasDeFrost.Raca, logou ? $"vaga 1: '{vaga.RacaSorteada}'" : $"nao logou: {c[0].Recusa}");
			if (logou)
			{
				c[0].CriarPersonagem(0, FichaDaVagaSorteada("Gelinho", vaga));
				mesa.Bombeia(() => c[0].NoMundo || c[0].Recusa != null, 8000);
			}
			ServerPlayer? gelinho = mesa.CorpoDe(ContasDaTelaCinza[0]);
			AfirmarTci("TC2b. ...e a ficha que a tela manda (a raca da vaga, o mundo dela) NASCE: um Frost Demon no mundo, sem recusa",
				c[0].NoMundo && c[0].Recusa == null && gelinho is { Race: FormasDeFrost.Raca }, $"recusa: {c[0].Recusa ?? "nenhuma"}; corpo {gelinho?.Race ?? "nenhum"}");
			if (c[0].NoMundo) mesa.SaiDoJogo(c[0], ContasDaTelaCinza[0]);

			GravarContaComOSorteioVelho(ContasDaTelaCinza[1]);
			string? recusaDeAntes = null, vagaDeAntes = null;
			bool nasceuDeAntes = false;
			AccountStore.SorteioVelhoFicaDeTeste = true;
			try
			{
				if (mesa.Entra(c[1]) && mesa.Loga(c[1], ContasDaTelaCinza[1]) && c[1].Slots is { Count: > 0 } slots)
				{
					vagaDeAntes = slots[0].RacaSorteada;
					c[1].CriarPersonagem(0, FichaDaVagaSorteada("Gelado", slots[0]));
					mesa.Bombeia(() => c[1].NoMundo || c[1].Recusa != null, 8000);
					recusaDeAntes = c[1].Recusa;
					nasceuDeAntes = c[1].NoMundo;
				}
			}
			finally { AccountStore.SorteioVelhoFicaDeTeste = false; }
			AfirmarTci("TC2c. CONTRA-EXEMPLO (defeito injetado: o sorteio velho fica): a lista diz \"Icer\" e a mesma criacao e RECUSADA (a tela cinza de quem tinha conta de antes da v0.4.3) -- a regua REPROVA",
				!nasceuDeAntes && vagaDeAntes == FormasDeFrost.RacaLegada && recusaDeAntes != null,
				$"vaga 1: '{vagaDeAntes}'; recusa: {recusaDeAntes ?? "nenhuma"}; nasceu: {nasceuDeAntes}");
			if (c[1].NoMundo) mesa.SaiDoJogo(c[1], ContasDaTelaCinza[1]);
			ModoDeRacas = modoAntes;

			// ============================================================ TC3) a excecao na criacao responde
			GD.Print("[telacinza] -- TC3) a excecao na criacao responde com uma recusa --");
			bool logou3 = mesa.Entra(c[2]) && mesa.Loga(c[2], ContasDaTelaCinza[2]) && c[2].Slots is { Count: > 0 };
			List<SlotInfo>? listaAntes = c[2].Slots;
			UltimoErroDaCriacao = "";
			CriacaoEstouraDeTeste = true;
			try
			{
				if (logou3)
				{
					c[2].CriarPersonagem(0, FichaDeClienteCru("Estouro"));
					mesa.Bombeia(() => c[2].NoMundo || (c[2].Recusa != null && !ReferenceEquals(c[2].Slots, listaAntes)), 4000);
				}
			}
			finally { CriacaoEstouraDeTeste = false; }
			AfirmarTci("TC3a. a criacao que ESTOURA no servidor e RECUSADA com o motivo generico -- o cliente nao fica esperando na cobertura",
				logou3 && !c[2].NoMundo && c[2].Recusa == MotivoDoErroNaCriacao, $"recusa: {c[2].Recusa ?? "NENHUMA"}");
			AfirmarTci("TC3b. ...e o erro INTEIRO vai pro log do servidor (a mensagem e a pilha)",
				UltimoErroDaCriacao.Contains("estourou de proposito") && UltimoErroDaCriacao.Contains(nameof(CriarOuRecusar)),
				UltimoErroDaCriacao.Split('\n')[0]);
			AfirmarTci("TC3c. ...e a lista de vagas volta junto da recusa, com a vaga ainda vazia (o que a selecao por baixo da criacao mostra)",
				!ReferenceEquals(c[2].Slots, listaAntes) && c[2].Slots is { Count: > 0 } s3 && !s3[0].Ocupado,
				ReferenceEquals(c[2].Slots, listaAntes) ? "a lista NAO voltou" : $"vaga 1 ocupada: {c[2].Slots?[0].Ocupado}");
			if (logou3 && !c[2].NoMundo)
			{
				c[2].CriarPersonagem(0, FichaDeClienteCru("Estouro"));
				mesa.Bombeia(() => c[2].NoMundo, 8000);
			}
			AfirmarTci("TC3d. ...e o mesmo jogador, sem sair da tela, cria de novo e entra no mundo", c[2].NoMundo && mesa.CorpoDe(ContasDaTelaCinza[2]) != null);
			if (c[2].NoMundo) mesa.SaiDoJogo(c[2], ContasDaTelaCinza[2]);

			bool logouCalado = mesa.Entra(c[3]) && mesa.Loga(c[3], ContasDaTelaCinza[3]);
			ErroDaCriacaoCaladoDeTeste = CriacaoEstouraDeTeste = true;
			try
			{
				if (logouCalado)
				{
					c[3].CriarPersonagem(0, FichaDeClienteCru("Calado"));
					mesa.Bombeia(() => c[3].NoMundo || c[3].Recusa != null, 2500);
				}
			}
			finally { ErroDaCriacaoCaladoDeTeste = CriacaoEstouraDeTeste = false; }
			AfirmarTci("TC3e. CONTRA-EXEMPLO (defeito injetado: o erro calado, o jogo de antes): a criacao que estoura nao recebe resposta nenhuma em 2,5 s -- a regua REPROVA",
				logouCalado && !c[3].NoMundo && c[3].Recusa != MotivoDoErroNaCriacao, $"recusa: {c[3].Recusa ?? "nenhuma"}");

			// ============================================================ TC4) quem ja entrou nao e recusado
			GD.Print("[telacinza] -- TC4) a excecao com o corpo ja no mundo nao e recusada --");
			bool logou4 = mesa.Entra(c[4]) && mesa.Loga(c[4], ContasDaTelaCinza[4]);
			UltimoErroDaCriacao = "";
			CriacaoEstouraDepoisDeEntrarDeTeste = true;
			try
			{
				if (logou4)
				{
					c[4].CriarPersonagem(0, FichaDeClienteCru("Dentro"));
					mesa.Bombeia(() => c[4].NoMundo, 8000);
					mesa.Bombeia(() => c[4].Recusa != null, 600);   // a recusa que viesse depois do mundo
				}
			}
			finally { CriacaoEstouraDepoisDeEntrarDeTeste = false; }
			AfirmarTci("TC4a. a excecao com o corpo JA no mundo nao manda recusa (o `JoinAccepted` ja saiu: a criacao reabriria por cima do mundo) -- e o erro vai pro log do mesmo jeito",
				logou4 && c[4].NoMundo && c[4].Recusa == null && UltimoErroDaCriacao.Contains("com o corpo ja no mundo"),
				$"no mundo {c[4].NoMundo}; recusa: {c[4].Recusa ?? "nenhuma"}; log: {UltimoErroDaCriacao.Split('\n')[0]}");
			if (c[4].NoMundo) mesa.SaiDoJogo(c[4], ContasDaTelaCinza[4]);

			bool logou5 = mesa.Entra(c[5]) && mesa.Loga(c[5], ContasDaTelaCinza[5]);
			RecusaDepoisDeEntrarDeTeste = CriacaoEstouraDepoisDeEntrarDeTeste = true;
			try
			{
				if (logou5)
				{
					c[5].CriarPersonagem(0, FichaDeClienteCru("Dentra"));
					mesa.Bombeia(() => c[5].NoMundo, 8000);
					mesa.Bombeia(() => c[5].Recusa != null, 600);
				}
			}
			finally { RecusaDepoisDeEntrarDeTeste = CriacaoEstouraDepoisDeEntrarDeTeste = false; }
			AfirmarTci("TC4b. CONTRA-EXEMPLO (defeito injetado: a recusa tambem pra quem ja entrou): a recusa chega DEPOIS do mundo -- a regua REPROVA",
				logou5 && !(c[5].NoMundo && c[5].Recusa == null), $"no mundo {c[5].NoMundo}; recusa: {c[5].Recusa ?? "nenhuma"}");
			if (c[5].NoMundo) mesa.SaiDoJogo(c[5], ContasDaTelaCinza[5]);

			// ============================================================ TC5) o "Jogar" tambem responde
			GD.Print("[telacinza] -- TC5) o \"Jogar\" de quem ja tem personagem responde a excecao --");
			// O PERSONAGEM QUE JA EXISTE, pelo fio de producao: cria, entra e sai (o `Drop` grava) -- e loga de novo, parado na selecao.
			bool JaTemPersonagem(int i, string nome)
			{
				ClienteCruDeTeste cc = c[i];
				string conta = ContasDaTelaCinza[i];
				if (!mesa.Entra(cc) || !mesa.Loga(cc, conta)) return false;
				cc.CriarPersonagem(0, FichaDeClienteCru(nome));
				if (!mesa.Bombeia(() => cc.NoMundo || cc.Recusa != null, 8000) || !cc.NoMundo || !mesa.SaiDoJogo(cc, conta)) return false;
				return mesa.Entra(cc) && mesa.Loga(cc, conta) && cc.Slots is { Count: > 0 } vagas && vagas[0].Ocupado;
			}

			bool pronto6 = JaTemPersonagem(6, "Jogador");
			UltimoErroDoJogar = "";
			JogarEstouraDeTeste = true;
			try
			{
				if (pronto6)
				{
					c[6].EscolherSlot(0);
					mesa.Bombeia(() => c[6].NoMundo || c[6].Recusa != null, 4000);
				}
			}
			finally { JogarEstouraDeTeste = false; }
			AfirmarTci("TC5a. o \"Jogar\" que ESTOURA no servidor e RECUSADO com o motivo generico -- o cliente nao fica esperando na cobertura",
				pronto6 && !c[6].NoMundo && c[6].Recusa == MotivoDoErroAoEntrar, $"recusa: {c[6].Recusa ?? "NENHUMA"}");
			AfirmarTci("TC5b. ...e o erro INTEIRO vai pro log do servidor (a mensagem e a pilha)",
				UltimoErroDoJogar.Contains("Jogar estourou de proposito") && UltimoErroDoJogar.Contains(nameof(EntrarOuRecusar)),
				UltimoErroDoJogar.Split('\n')[0]);
			if (pronto6 && !c[6].NoMundo)
			{
				c[6].EscolherSlot(0);
				mesa.Bombeia(() => c[6].NoMundo, 8000);
			}
			AfirmarTci("TC5c. ...e o mesmo jogador aperta \"Jogar\" de novo e entra no mundo", c[6].NoMundo && mesa.CorpoDe(ContasDaTelaCinza[6]) != null);
			if (c[6].NoMundo) mesa.SaiDoJogo(c[6], ContasDaTelaCinza[6]);

			bool pronto7 = JaTemPersonagem(7, "Silencio");
			ErroDoJogarCaladoDeTeste = JogarEstouraDeTeste = true;
			try
			{
				if (pronto7)
				{
					c[7].EscolherSlot(0);
					mesa.Bombeia(() => c[7].NoMundo || c[7].Recusa != null, 2500);
				}
			}
			finally { ErroDoJogarCaladoDeTeste = JogarEstouraDeTeste = false; }
			AfirmarTci("TC5d. CONTRA-EXEMPLO (defeito injetado: o erro do \"Jogar\" calado, o jogo de antes): nenhuma resposta em 2,5 s -- a regua REPROVA",
				pronto7 && !c[7].NoMundo && c[7].Recusa != MotivoDoErroAoEntrar, $"recusa: {c[7].Recusa ?? "nenhuma"}");

			bool pronto8 = JaTemPersonagem(8, "Entrou");
			UltimoErroDoJogar = "";
			JogarEstouraDepoisDeEntrarDeTeste = true;
			try
			{
				if (pronto8)
				{
					c[8].EscolherSlot(0);
					mesa.Bombeia(() => c[8].NoMundo, 8000);
					mesa.Bombeia(() => c[8].Recusa != null, 600);   // a recusa que viesse depois do mundo
				}
			}
			finally { JogarEstouraDepoisDeEntrarDeTeste = false; }
			AfirmarTci("TC5e. o \"Jogar\" que estoura com o corpo JA no mundo nao manda recusa -- e o erro vai pro log do mesmo jeito",
				pronto8 && c[8].NoMundo && c[8].Recusa == null && UltimoErroDoJogar.Contains("com o corpo ja no mundo"),
				$"no mundo {c[8].NoMundo}; recusa: {c[8].Recusa ?? "nenhuma"}; log: {UltimoErroDoJogar.Split('\n')[0]}");
			if (c[8].NoMundo) mesa.SaiDoJogo(c[8], ContasDaTelaCinza[8]);

			bool pronto9 = JaTemPersonagem(9, "Entrada");
			RecusaDepoisDeEntrarDeTeste = JogarEstouraDepoisDeEntrarDeTeste = true;
			try
			{
				if (pronto9)
				{
					c[9].EscolherSlot(0);
					mesa.Bombeia(() => c[9].NoMundo, 8000);
					mesa.Bombeia(() => c[9].Recusa != null, 600);
				}
			}
			finally { RecusaDepoisDeEntrarDeTeste = JogarEstouraDepoisDeEntrarDeTeste = false; }
			AfirmarTci("TC5f. CONTRA-EXEMPLO (defeito injetado: a recusa tambem pra quem ja entrou): a recusa do \"Jogar\" chega DEPOIS do mundo -- a regua REPROVA",
				pronto9 && !(c[9].NoMundo && c[9].Recusa == null), $"no mundo {c[9].NoMundo}; recusa: {c[9].Recusa ?? "nenhuma"}");
			if (c[9].NoMundo) mesa.SaiDoJogo(c[9], ContasDaTelaCinza[9]);
		}
		catch (Exception e)
		{
			AfirmarTci("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			AccountStore.SorteioVelhoFicaDeTeste = false;
			ErroDaCriacaoCaladoDeTeste = RecusaDepoisDeEntrarDeTeste = false;
			CriacaoEstouraDeTeste = CriacaoEstouraDepoisDeEntrarDeTeste = false;
			ErroDoJogarCaladoDeTeste = JogarEstouraDeTeste = JogarEstouraDepoisDeEntrarDeTeste = false;
			ModoDeRacas = modoAntes;
			mesa.Fecha();
			ApagarContasDaTelaCinza();
			_tciRodando = false;
			GD.Print($"[telacinza] ================ {_tciOk} OK, {_tciFalhou} FALHA(S) ================");
		}
	}
}
