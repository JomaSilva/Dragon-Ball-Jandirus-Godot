using Godot;
using Jandirus.Core.Races;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA TELA CINZA (`--diagtelacinza`, 2026-10-23). O relato do dono: *"alguns jogadores caem em uma tela cinza na criaçao"*; a
/// decisao dele: *"So mostrar o motivo"* -- toda recusa do servidor depois do "Entrar no mundo" reabre a criacao com o rascunho intacto
/// e o motivo escrito, e o nome de um NPC continua bloqueando. Ver `Boot.RecusaDaCriacao.cs` e `CreationScreen.ReabrirComOMotivo`.
///
/// SEM `--host`, como a `--diagmutante`: o robo sobe o servidor deste processo e entra pelas TELAS DE PRODUCAO do `Boot` -- o login, a
/// selecao e a criacao, apertadas com o mesmo `Pressed` do dedo do jogador -- e manda a ficha pelo `CreateChar` de verdade.
///
///   T3. (so em portugues) A CRIACAO DO FILHO DESISTIDA: "Nascer como filho(a)" (um meio-Saiyajin), desistir, "Criar outro personagem" --
///       a criacao comum volta ao mundo de fabrica, com a raca da ficha marcada na tela (foto `telacinza-0-raca-depois-do-filho`).
///       CONTRA-EXEMPLO `CreationScreen.MundoDoFilhoFicaDeTeste`: o meio-Saiyajin fica na ficha, com o Humano marcado (foto
///       `telacinza-0b-defeito-raca-do-filho`).
///   T1. O NOME DE UM MORADOR DO MUNDO (um cidadao do povoamento, que esta no `_players`): o servidor recusa pelo nome e a criacao VOLTA,
///       na etapa do nome, com o motivo no rotulo vermelho logo acima do "Entrar no mundo" e o rascunho intacto (foto
///       `telacinza-1-motivo-do-nome`). CONTRA-EXEMPLO `Boot.RecusaSoNoLoginDeTeste`: a tela cinza (foto `telacinza-1b-defeito-tela-cinza`).
///       Em ingles (`--emingles`): o motivo sai em ingles; CONTRA-EXEMPLO `Idioma.TelaSemTraducaoDeTeste`.
///   T2. O ERRO DO SERVIDOR (a criacao que estoura, `GameServer.CriacaoEstouraDeTeste`): a recusa generica tambem reabre, e a lista de
///       vagas que chega junto nao cobre a criacao (foto `telacinza-2-erro-do-servidor`). CONTRA-EXEMPLO `Boot.ListaCobreACriacaoDeTeste`
///       (foto `telacinza-2b-defeito-lista-por-cima`). Em ingles, o motivo generico em ingles.
///   T5. O NOME TROCADO ENTRA NO MUNDO de verdade (foto `telacinza-3-no-mundo`).
///
/// (2026-10-24, o dono: "Consertar") T3b: depois da criacao do filho desistida, a idade volta aos 18 (CONTRA-EXEMPLO
/// `CreationScreen.IdadeDoBebeFicaDeTeste`). T1d (em ingles): a descricao da raca sai INTEIRA em ingles -- e T1g-T1i (r4) as outras tres
/// terminacoes: o Saiyajin que escolhe, o Frost Demon sorteado e o filho (fotos `telacinza-en-0-*`). E a rodada `--jogar`:
///   T6. O "JOGAR" DE QUEM JA TEM PERSONAGEM (criado pelo fio antes de o cliente logar): o servidor que estoura no `Entrar`
///       (`GameServer.JogarEstouraDeTeste`) responde com a recusa generica, e a SELECAO mostra o motivo em vermelho (foto
///       `telacinza-jogar-1-motivo`); CONTRA-EXEMPLO `Boot.RecusaDoJogarSoNoLoginDeTeste` (foto `telacinza-jogar-1b-defeito-sem-motivo`);
///       em ingles, o motivo em ingles (foto `telacinza-jogar-2-motivo-em-ingles`); e o "Jogar" seguinte entra (foto `telacinza-jogar-3-no-mundo`).
///
///     Godot --path . --diagtelacinza [--emingles | --jogar] --resolution 1280x720 --rede 10811 --horateste 0.5 --semfauna
/// Fotos `telacinza-*.png` (`telacinza-en-*.png` em ingles, `telacinza-jogar-*.png` no "Jogar") no `user://`. Sem janela nao ha foto,
/// mas as medidas saem.
/// </summary>
public partial class RoboDaTelaCinza : Node
{
	private const string Senha = "dtelacinza";
	private const string Prefixo = "[dtelacinza] ";
	private const string Historia = "Uma ficha que o servidor recusou, e que a tela devolveu inteira.";
	/// <summary>A recusa do nome, como o `CreateChar` a escreve.</summary>
	private const string RecusaDoNome = "ja tem alguem em jogo com esse nome";
	/// <summary>A linha em ingles do motivo generico (`Assets/Data/lingua-en.json`) -- e o que quem joga em ingles le.</summary>
	private const string ErroDoServidorEmIngles = "server error while creating the character -- try again (the error was written to the server log)";
	/// <summary>(2026-10-24) A linha em ingles do motivo generico do "Jogar".</summary>
	private const string ErroAoEntrarEmIngles = "server error while entering the world -- try again (the error was written to the server log)";

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private bool _ingles, _jogar;
	private string Conta => _jogar ? "bancada_dtelacinza_jg" : _ingles ? "bancada_dtelacinza_en" : "bancada_dtelacinza";
	private string Foto(string nome) => (_jogar ? "telacinza-jogar-" : _ingles ? "telacinza-en-" : "telacinza-") + nome;

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _fotos = [];
	private readonly List<string> _recusas = [];
	private List<SlotInfo> _slots = [];
	private int _listas, _bebe;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready()
	{
		_ingles = Array.IndexOf(OS.GetCmdlineArgs(), "--emingles") >= 0;
		_jogar = Array.IndexOf(OS.GetCmdlineArgs(), "--jogar") >= 0;
		// O SERVIDOR SOBE AQUI, como na `--diagmutante`: sem `--host` ninguem o levanta, e o robo entra pelo login como o jogador entra.
		if (S is { } srv && !srv.Running && !srv.Start(Porta()))
		{
			GD.PushError(Prefixo + "nao consegui abrir a porta -- ha outra bancada viva?");
			return;
		}
		if (C is not { } cli) return;
		cli.SlotsRecebidos += AoReceberSlots;
		cli.Rejected += AoSerRecusado;
		_ = Rodar();
	}

	public override void _ExitTree()
	{
		if (C is { } cli)
		{
			cli.SlotsRecebidos -= AoReceberSlots;
			cli.Rejected -= AoSerRecusado;
		}
		DesligarDefeitos();
	}

	private static void DesligarDefeitos()
	{
		Boot.RecusaSoNoLoginDeTeste = false;
		Boot.ListaCobreACriacaoDeTeste = false;
		CreationScreen.MundoDoFilhoFicaDeTeste = false;
		CreationScreen.IdadeDoBebeFicaDeTeste = false;
		Boot.RecusaDoJogarSoNoLoginDeTeste = false;
		Idioma.TelaSemTraducaoDeTeste = false;
		Jandirus.Server.GameServer.CriacaoEstouraDeTeste = false;
		Jandirus.Server.GameServer.JogarEstouraDeTeste = false;
	}

	private static int Porta()
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, "--rede");
		return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out int p) && p > 0 ? p : Protocol.DefaultPort;
	}

	private void AoReceberSlots(List<SlotInfo> slots)
	{
		_slots = slots;
		_listas++;
	}

	private void AoSerRecusado(string motivo) => _recusas.Add(motivo);

	private async Tarefa Rodar()
	{
		try
		{
			if (_jogar) await PercursoDoJogar();
			else await Percurso();
		}
		catch (Exception e)
		{
			Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})");
			GD.Print(e.ToString());
		}
		finally
		{
			DesligarDefeitos();
			if (_bebe > 0) S?.TirarOBebeDaFilaDeTeste(_bebe);
		}
		Fechar();
	}

	private async Tarefa Percurso()
	{
		GD.Print(Prefixo + $"===== A TELA CINZA DA CRIACAO (dono, 2026-10-23){(_ingles ? " -- EM INGLES" : "")} =====");
		if (C is not { } cli || S is not { } srv || GetParent() is not Boot boot)
		{
			Conferir(false, "(montagem) o cliente, o servidor e o Boot deste processo existem");
			return;
		}
		await Segundos(1.0);
		if (_ingles)
		{
			Idioma.Aplicar(Idioma.Ingles);
			await Quadros(3);
		}

		// O BEBE (so em portugues): a fila limpa e um meio-Saiyajin de Goku e Chichi -- a oferta chega no fim da lista de vagas.
		if (!_ingles)
		{
			srv.EsvaziarAFilaDeBebesDeTeste();
			_bebe = srv.PorUmBebeDeTeste("Goku", "Chichi", "Saiyan", "Human", 400_000, 24_000);
		}
		else
		{
			// (r4) em ingles, um Saiyajin filho de dois Saiyajins da mesma linhagem: a linhagem dele vem dos pais (a terminacao do filho)
			srv.EsvaziarAFilaDeBebesDeTeste();
			_bebe = srv.PorUmBebeDeTeste("Goku", "Gine", "Saiyan", "Saiyan", 400_000, 24_000);
		}

		cli.Conectar("127.0.0.1", Porta(), Conta, Senha);
		if (!await Ate(() => _listas > 0, 40)) { Conferir(false, "(montagem) a conta abriu e a lista de vagas chegou"); return; }
		// A PASTA DE SAVES DA BANCADA E A MESMA DE UMA RODADA PRA OUTRA: o personagem que entrou no mundo da ultima vez sai.
		for (int i = 0; i < _slots.Count; i++)
		{
			if (!_slots[i].Ocupado) continue;
			int n = _listas;
			cli.SendDeleteChar(i, _slots[i].Nome);
			await Ate(() => _listas > n, 6);
		}
		Conferir(_slots.Count > 0 && _slots.All(s => !s.Ocupado), "(montagem) a conta comeca sem personagem");

		string morador = "";
		await Ate(() => (morador = srv.NomeDeUmMoradorDeTeste()).Length > 0, 60);
		Conferir(morador.Length > 0, $"(montagem) o povoamento ja pos moradores no mundo -- um deles: '{morador}'");
		if (morador.Length == 0) return;

		CharacterSelect? selecao = await Achar<CharacterSelect>(GetTree().Root, s => s.Visible, 10);
		TelaDeCarregamento? capa = await Achar<TelaDeCarregamento>(GetTree().Root, _ => true, 2);
		if (selecao == null) { Conferir(false, "(montagem) a selecao de producao abriu"); return; }

		// ---------------------------------------------------------- T3. a criacao do filho desistida
		if (!_ingles)
		{
			CreationScreen? doFilho = await AbrirComoFilho(selecao);
			string racaDoFilho = doFilho?.FichaDeTeste.Raca ?? "";
			Conferir(doFilho != null && racaDoFilho == Filhos.MeioSaiyajin,
					 $"(montagem) 'Nascer como filho(a)' abre a criacao do filho, com a raca dos pais na ficha [{racaDoFilho}]");
			if (doFilho == null) return;
			await VoltarPraSelecao(doFilho);
			await AbrirComum(selecao);
			await Quadros(3);
			var limpo = doFilho.MundoDaFichaDeTeste;
			var idade = doFilho.FilhoDeTeste;
			await IrAte(doFilho, "ESCOLHA SUA RAÇA", escolher: false);
			await Segundos(0.3);
			Fotografar(Foto("0-raca-depois-do-filho"));
			Conferir(ReguaDoMundo(limpo),
					 "T3a. 'Nascer como filho(a)', desistir e 'Criar outro personagem': a criacao COMUM volta ao mundo de fabrica -- a raca da ficha e "
					 + $"uma do mundo marcado, e e ela a marcada na tela [{Mundo(limpo)}]");
			Conferir(ReguaDaIdade(idade),
					 "T3b. ...e a IDADE volta aos 18 de sempre, destravada: o zero do bebe nao fica (o dono: \"Consertar\") "
					 + $"[{idade.Idade} ano(s), travada {idade.IdadeTravada}, filho do bebe #{idade.FilhoDe}]");

			// CONTRA-EXEMPLO: o mesmo caminho, com o mundo, a raca e a idade da criacao do filho ficando na criacao comum (cada regua le o
			// seu pedaco -- o mundo e a raca de um lado, a idade do outro -- e cada botao de defeito derruba so o seu)
			await VoltarPraSelecao(doFilho);
			CreationScreen.MundoDoFilhoFicaDeTeste = true;
			CreationScreen.IdadeDoBebeFicaDeTeste = true;
			try
			{
				await AbrirComoFilho(selecao);
				await VoltarPraSelecao(doFilho);
				await AbrirComum(selecao);
				await Quadros(3);
				var sujo = doFilho.MundoDaFichaDeTeste;
				var idadeSuja = doFilho.FilhoDeTeste;
				await IrAte(doFilho, "ESCOLHA SUA RAÇA", escolher: false);
				await Segundos(0.3);
				Fotografar(Foto("0b-defeito-raca-do-filho"));
				Conferir(!ReguaDoMundo(sujo),
						 "T3a. CONTRA-EXEMPLO (defeito injetado: o mundo e a raca da criacao do filho ficam, o jogo de antes): o meio-Saiyajin fica na "
						 + $"ficha com o Humano marcado na tela -- a regua REPROVA [{Mundo(sujo)}]");
				Conferir(!ReguaDaIdade(idadeSuja),
						 "T3b. CONTRA-EXEMPLO (defeito injetado: a idade do bebe lida depois de o minimo subir, o jogo de antes): a criacao comum abre "
						 + $"com {idadeSuja.Idade} ano(s) -- a regua REPROVA");
			}
			finally
			{
				CreationScreen.MundoDoFilhoFicaDeTeste = false;
				CreationScreen.IdadeDoBebeFicaDeTeste = false;
			}
			// A CRIACAO DE T1 PARTE DO ZERO: o defeito de cima deixou a raca e a idade do bebe na tela, e so um "Nascer como filho(a)" e um
			// "Criar outro personagem" SEM defeito as tiram (e o caminho de producao, e nao um palco).
			await VoltarPraSelecao(doFilho);
			await AbrirComoFilho(selecao);
			await VoltarPraSelecao(doFilho);
		}

		// ---------------------------------------------------------- (r4) T1g-T1i. as outras tres terminacoes da descricao, em ingles
		// A frase das linhagens engolia a terminacao (o molde "Linhagens possiveis: {0}{1}." levava a frase seguinte no pedaco), e a tela
		// de ingles mostrava em portugues a do Saiyajin (escolhe), a do Frost Demon (ja sorteada) e a do filho (vem dos pais).
		if (_ingles)
		{
			CreationScreen? tela = await AbrirComum(selecao);
			if (tela == null) { Conferir(false, "(montagem) a selecao de producao abre a criacao no 'Criar personagem'"); return; }
			for (int i = 0; i < 6 && tela.TituloDaEtapaDeTeste != "ESCOLHA SUA RAÇA"; i++)
			{
				if (tela.TituloDaEtapaDeTeste == "DE ONDE VEM O SEU POVO?") Apertar(Botao(tela, "Vegeta"));
				await Quadros(2);
				Apertar(Botao(tela, "Avançar"));
				await Segundos(0.25);
			}
			Apertar(Botao(tela, NomeDaRaca.Bonito("Saiyan")));
			await Quadros(3);
			string saiyajin = DescricaoDesenhada(tela);
			Fotografar(Foto("0-saiyajin"));
			Apertar(Botao(tela, NomeDaRaca.Bonito(FormasDeFrost.Raca)));
			await Quadros(3);
			string frost = DescricaoDesenhada(tela);
			Fotografar(Foto("0-frost"));
			await VoltarPraSelecao(tela);
			await AbrirComoFilho(selecao);
			await Quadros(3);
			string filho = DescricaoDesenhada(tela);
			Fotografar(Foto("0-filho"));
			await VoltarPraSelecao(tela);
			Conferir(!Idioma.Tradutor.ParecePortugues(saiyajin),
					 $"T1g. a descricao do Saiyajin (quem ESCOLHE a linhagem) sai INTEIRA em ingles ['{saiyajin}']");
			Conferir(!Idioma.Tradutor.ParecePortugues(frost),
					 $"T1h. ...e a do Frost Demon (a classe JA SORTEADA) tambem ['{frost}']");
			Conferir(!Idioma.Tradutor.ParecePortugues(filho),
					 $"T1i. ...e a do FILHO (a linhagem que vem dos pais) tambem ['{filho}']");
		}

		// ---------------------------------------------------------- T1. o nome de um morador do mundo
		CreationScreen? criacao = await AbrirComum(selecao);
		if (criacao == null) { Conferir(false, "(montagem) a selecao de producao abre a criacao no 'Criar personagem'"); return; }
		bool naIdentidade = await IrAte(criacao, "QUEM É VOCÊ?", escolher: true);
		Escrever(criacao, morador, Historia);
		await Segundos(0.3);
		var rascunho = criacao.RascunhoDeTeste;
		var ficha = criacao.FichaDeTeste;
		Conferir(naIdentidade && rascunho.Pecas >= 1 && ficha.Raca == "Human",
				 $"(montagem) a criacao chega em 'QUEM É VOCÊ?' com um Humano vestido e o nome do morador [{ficha.Raca}, {rascunho.Pecas} peca(s), '{rascunho.Nome}']");

		int recusas = _recusas.Count;
		Apertar(Botao(criacao, "Entrar no mundo"));
		bool recusou = await Ate(() => _recusas.Count > recusas, 10);
		bool voltou = await Ate(() => criacao.Visible, 3);
		await Ate(() => capa is not { NoAr: true }, 3);
		await Quadros(4);
		Fotografar(Foto("1-motivo-do-nome"));
		string recusa = recusou ? _recusas[^1] : "";
		var rotulo = Rotulo(criacao);
		Conferir(recusou && voltou && criacao.TituloDaEtapaDeTeste == "QUEM É VOCÊ?" && capa is not { NoAr: true },
				 "T1a. a recusa do 'Entrar no mundo' REABRE a criacao, na ultima etapa ('QUEM É VOCÊ?', a do nome), com a cobertura no chao "
				 + $"[recusa: '{recusa}'; criacao na tela: {criacao.Visible}; etapa '{criacao.TituloDaEtapaDeTeste}'; capa no ar: {capa?.NoAr}]");
		Conferir(recusa == RecusaDoNome,
				 $"T1r. o servidor recusou PELO NOME: o morador do mundo conta (o nome de NPC continua bloqueando, decisao do dono) ['{recusa}']");
		Conferir(rotulo.Visivel && criacao.ErroDeTeste == recusa && rotulo.Vermelho && rotulo.Folga is >= 0f and <= 80f,
				 "T1b. o MOTIVO esta escrito e VISIVEL: no rotulo vermelho da propria criacao, logo acima do 'Entrar no mundo' "
				 + $"[{Descrito(rotulo, criacao.ErroDeTeste)}]");
		Conferir(!voltou || (criacao.RascunhoDeTeste == rascunho && criacao.FichaDeTeste == ficha),
				 "T1c. ...e o RASCUNHO do jogador esta como ele deixou: o nome, a historia, a roupa e a raca "
				 + $"[antes '{rascunho.Nome}', {rascunho.Pecas} peca(s), {ficha.Raca}; agora '{criacao.RascunhoDeTeste.Nome}', "
				 + $"{criacao.RascunhoDeTeste.Pecas} peca(s), {criacao.FichaDeTeste.Raca}]");
		if (_ingles)
		{
			string ingles = Idioma.Tradutor.Traduzir(recusa);
			Conferir(rotulo.Desenhado == ingles && rotulo.Desenhado != recusa && !Idioma.Tradutor.ParecePortugues(rotulo.Desenhado),
					 $"T1e. ...e o motivo sai em INGLES pra quem joga em ingles [desenhado '{rotulo.Desenhado}'; o dicionario diz '{ingles}']");
			// (2026-10-24) A DESCRICAO DA RACA, logo acima do motivo: a frase das linhagens engolia a do combate ("A sua e revelada...") e a
			// tela de ingles a mostrava em portugues -- a linha nova do dicionario e o molde inteiro.
			string descricao = DescricaoDesenhada(criacao);
			Conferir(!Idioma.Tradutor.ParecePortugues(descricao),
					 $"T1d. ...e a DESCRICAO da raca, logo acima, sai INTEIRA em ingles (as linhagens e a frase do combate) ['{descricao}']");

			// CONTRA-EXEMPLO: a traducao fora do Godot -- o mesmo rotulo desenha o portugues (e a mesma descricao tambem)
			Idioma.TelaSemTraducaoDeTeste = true;
			try
			{
				Idioma.Aplicar(Idioma.Portugues);
				Idioma.Aplicar(Idioma.Ingles);
				await Quadros(4);
				Fotografar(Foto("1b-defeito-sem-traducao"));
				var semTraducao = Rotulo(criacao);
				Conferir(!(semTraducao.Desenhado == ingles && semTraducao.Desenhado != recusa),
						 "T1e. CONTRA-EXEMPLO (defeito injetado: a traducao fora do Godot): o mesmo rotulo desenha o motivo em portugues -- a regua "
						 + $"REPROVA ['{semTraducao.Desenhado}']");
				string descricaoSemTraducao = DescricaoDesenhada(criacao);
				Conferir(Idioma.Tradutor.ParecePortugues(descricaoSemTraducao),
						 "T1d. CONTRA-EXEMPLO (defeito injetado: a traducao fora do Godot): a mesma descricao sai em portugues -- a regua REPROVA "
						 + $"['{descricaoSemTraducao}']");
			}
			finally
			{
				Idioma.TelaSemTraducaoDeTeste = false;
				Idioma.Aplicar(Idioma.Portugues);
				Idioma.Aplicar(Idioma.Ingles);
				await Quadros(3);
			}
		}
		else
		{
			// CONTRA-EXEMPLO: a recusa so no rotulo do login, o jogo de antes -- a tela cinza
			Boot.RecusaSoNoLoginDeTeste = true;
			try
			{
				int antes = _recusas.Count;
				Apertar(Botao(criacao, "Entrar no mundo"));
				await Ate(() => _recusas.Count > antes, 10);
				await Ate(() => capa is not { NoAr: true }, 3);
				await Segundos(0.5);
				Fotografar(Foto("1b-defeito-tela-cinza"));
				var cinza = Rotulo(criacao);
				bool nada = !criacao.Visible && !selecao.Visible && !boot.MenuPrincipalNaTela;
				Conferir(!(criacao.Visible && cinza.Visivel && criacao.ErroDeTeste.Length > 0) && nada,
						 "T1x. CONTRA-EXEMPLO (defeito injetado: a recusa so no rotulo do login, o jogo de antes): sobra a TELA CINZA -- nem a criacao, "
						 + $"nem a selecao, nem o login, e o motivo em lugar nenhum -- a regua REPROVA [criacao {criacao.Visible}, selecao "
						 + $"{selecao.Visible}, login {boot.MenuPrincipalNaTela}; o login diz '{boot.EstadoDoMenuPrincipal}']");
			}
			finally { Boot.RecusaSoNoLoginDeTeste = false; }
			await DeVoltaATela(criacao, selecao);
		}

		// ---------------------------------------------------------- T2. o erro do servidor
		await DeVoltaATela(criacao, selecao);
		int recusas2 = _recusas.Count, listas2 = _listas;
		Jandirus.Server.GameServer.CriacaoEstouraDeTeste = true;
		try
		{
			Apertar(Botao(criacao, "Entrar no mundo"));
			await Ate(() => _recusas.Count > recusas2 && _listas > listas2, 10);
		}
		finally { Jandirus.Server.GameServer.CriacaoEstouraDeTeste = false; }
		await Ate(() => capa is not { NoAr: true }, 3);
		await Quadros(4);
		Fotografar(Foto("2-erro-do-servidor"));
		string recusa2 = _recusas.Count > recusas2 ? _recusas[^1] : "";
		bool voltou2 = criacao.Visible;
		var rotulo2 = Rotulo(criacao);
		Conferir(recusa2 == Jandirus.Server.GameServer.MotivoDoErroNaCriacao && _listas > listas2,
				 $"(montagem) a criacao que ESTOURA no servidor e recusada com o motivo generico, e a lista de vagas vem junto ['{recusa2}']");
		Conferir(voltou2 && rotulo2.Visivel && criacao.ErroDeTeste == recusa2,
				 $"T2a. o ERRO DO SERVIDOR tambem reabre a criacao, com o motivo generico na tela [{Descrito(rotulo2, criacao.ErroDeTeste)}]");
		Conferir(!voltou2 || !selecao.Visible,
				 $"T2b. ...e a lista de vagas que chega junto NAO cobre a criacao: a selecao se atualiza por baixo [selecao na tela: {selecao.Visible}]");
		if (_ingles)
			Conferir(rotulo2.Desenhado == ErroDoServidorEmIngles,
					 $"T2e. ...e o motivo generico sai em INGLES (a linha nova do dicionario) ['{rotulo2.Desenhado}']");
		else
		{
			// CONTRA-EXEMPLO: a lista volta por cima da criacao reaberta
			await DeVoltaATela(criacao, selecao);
			int recusas3 = _recusas.Count, listas3 = _listas;
			Boot.ListaCobreACriacaoDeTeste = true;
			Jandirus.Server.GameServer.CriacaoEstouraDeTeste = true;
			try
			{
				Apertar(Botao(criacao, "Entrar no mundo"));
				await Ate(() => _recusas.Count > recusas3 && _listas > listas3, 10);
				await Ate(() => capa is not { NoAr: true }, 3);
				await Quadros(4);
				Fotografar(Foto("2b-defeito-lista-por-cima"));
				Conferir(!(criacao.Visible && !selecao.Visible),
						 "T2b. CONTRA-EXEMPLO (defeito injetado: a lista que chega com a recusa aparece por cima): a selecao cobre a criacao reaberta, e o "
						 + $"motivo fica atras dela -- a regua REPROVA [criacao {criacao.Visible}, selecao {selecao.Visible}]");
			}
			finally
			{
				Boot.ListaCobreACriacaoDeTeste = false;
				Jandirus.Server.GameServer.CriacaoEstouraDeTeste = false;
			}
		}

		// ---------------------------------------------------------- T5. o nome trocado entra no mundo
		await DeVoltaATela(criacao, selecao);
		string novoNome = _ingles ? "Stubborn" : "Teimoso";
		Escrever(criacao, novoNome, null);
		await Segundos(0.3);
		int recusas5 = _recusas.Count;
		Apertar(Botao(criacao, "Entrar no mundo"));
		bool entrou = await Ate(() => C is { LocalId: > 0 } && World.Instancia != null, 40);
		await Segundos(2.5);
		Fotografar(Foto("3-no-mundo"));
		string? noMundo = srv.NoMundoDeTeste(Conta);
		Conferir(entrou && noMundo == novoNome && _recusas.Count == recusas5,
				 $"T5. com o nome TROCADO a mesma ficha entra no mundo de verdade ['{noMundo ?? "ninguem"}' no mundo; "
				 + $"{_recusas.Count - recusas5} recusa(s) depois]");
	}

	// =====================================================================
	// (2026-10-24) T6. O "JOGAR" -- a rodada `--jogar`
	// =====================================================================
	private async Tarefa PercursoDoJogar()
	{
		GD.Print(Prefixo + "===== A RECUSA DO \"JOGAR\" NA SELECAO (dono, 2026-10-24) =====");
		if (C is not { } cli || S is not { } srv)
		{
			Conferir(false, "(montagem) o cliente e o servidor deste processo existem");
			return;
		}
		await Segundos(1.0);

		// O PERSONAGEM QUE JA EXISTE, criado pelo fio ANTES de o cliente deste processo logar: o caminho da criacao do jogo termina no
		// mundo, e o "Jogar" e de quem volta. Se a pasta de saves da bancada ja o tem (a rodada de antes), e ele.
		string nome = srv.PersonagemPeloFioDeTeste(Porta(), Conta, Senha, "Veterano");
		Conferir(nome.Length > 0, $"(montagem) a conta tem um personagem na vaga 1, criado pelo fio de producao: '{nome}'");
		if (nome.Length == 0) return;

		cli.Conectar("127.0.0.1", Porta(), Conta, Senha);
		if (!await Ate(() => _listas > 0, 40)) { Conferir(false, "(montagem) a conta abriu e a lista de vagas chegou"); return; }
		CharacterSelect? selecao = await Achar<CharacterSelect>(GetTree().Root, s => s.Visible, 10);
		TelaDeCarregamento? capa = await Achar<TelaDeCarregamento>(GetTree().Root, _ => true, 2);
		Conferir(selecao != null && _slots.Count > 0 && _slots[0].Ocupado && Botao(selecao, "Jogar") != null,
				 $"(montagem) a selecao de producao mostra o personagem com o botao Jogar [{(_slots.Count > 0 ? _slots[0].Nome : "sem vagas")}]");
		if (selecao == null) return;
		await Segundos(0.5);

		// ---------------------------------------------------------- T6a. o erro do servidor no "Jogar"
		var a = await JogarComErro(selecao, capa);
		Fotografar(Foto("1-motivo"));
		Conferir(a.Recusa == Jandirus.Server.GameServer.MotivoDoErroAoEntrar && a.Selecao && !a.Mundo,
				 $"(montagem) o \"Jogar\" que ESTOURA no servidor e recusado com o motivo generico, e o jogador fica na selecao ['{a.Recusa}']");
		Conferir(ReguaDoMotivoNaSelecao(a),
				 "T6a. o MOTIVO da recusa do \"Jogar\" esta escrito e VISIVEL na propria selecao, em vermelho, com a cobertura no chao "
				 + $"[{DescritoNaSelecao(a)}]");

		// CONTRA-EXEMPLO: a recusa do "Jogar" so no rotulo do login -- o jogo de antes
		Boot.RecusaDoJogarSoNoLoginDeTeste = true;
		try
		{
			var x = await JogarComErro(selecao, capa);
			Fotografar(Foto("1b-defeito-sem-motivo"));
			Conferir(!ReguaDoMotivoNaSelecao(x) && x.Selecao && x.Recusa.Length > 0,
					 "T6x. CONTRA-EXEMPLO (defeito injetado: a recusa do \"Jogar\" so no rotulo escondido do login, o jogo de antes): o jogador volta "
					 + $"pra selecao SEM o motivo -- a regua REPROVA [{DescritoNaSelecao(x)}]");
		}
		finally { Boot.RecusaDoJogarSoNoLoginDeTeste = false; }

		// ---------------------------------------------------------- T6e. em ingles
		Idioma.Aplicar(Idioma.Ingles);
		await Quadros(3);
		var e = await JogarComErro(selecao, capa);
		Fotografar(Foto("2-motivo-em-ingles"));
		Conferir(ReguaDoMotivoNaSelecao(e) && e.Desenhado == ErroAoEntrarEmIngles,
				 $"T6e. ...e quem joga em ingles le o motivo em INGLES (a linha nova do dicionario) [{DescritoNaSelecao(e)}]");
		Idioma.Aplicar(Idioma.Portugues);
		await Quadros(3);

		// ---------------------------------------------------------- T6b. o "Jogar" seguinte entra
		int recusas = _recusas.Count;
		Apertar(Botao(selecao, "Jogar"));
		bool entrou = await Ate(() => C is { LocalId: > 0 } && World.Instancia != null, 40);
		await Segundos(2.5);
		Fotografar(Foto("3-no-mundo"));
		string? noMundo = srv.NoMundoDeTeste(Conta);
		Conferir(entrou && noMundo == nome && _recusas.Count == recusas,
				 $"T6b. o \"Jogar\" seguinte, sem o erro, entra no mundo de verdade ['{noMundo ?? "ninguem"}' no mundo; "
				 + $"{_recusas.Count - recusas} recusa(s) depois]");
	}

	/// <summary>
	/// O "Jogar" com o servidor estourando (`GameServer.JogarEstouraDeTeste`, a reproducao forcada): aperta, espera a recusa e a cobertura
	/// cair, e le a selecao -- a recusa que chegou, se ela e o mundo estao na tela, e o rotulo do motivo (o texto, o DESENHADO e a cor).
	/// </summary>
	private async System.Threading.Tasks.Task<(string Recusa, bool Selecao, bool Mundo, bool CapaNoChao, bool Visivel, string Texto,
		string Desenhado, bool Vermelho)> JogarComErro(CharacterSelect selecao, TelaDeCarregamento? capa)
	{
		int antes = _recusas.Count;
		Jandirus.Server.GameServer.JogarEstouraDeTeste = true;
		try
		{
			Apertar(Botao(selecao, "Jogar"));
			await Ate(() => _recusas.Count > antes, 10);
		}
		finally { Jandirus.Server.GameServer.JogarEstouraDeTeste = false; }
		bool capaNoChao = await Ate(() => capa is not { NoAr: true }, 3);
		await Quadros(4);
		Label? r = selecao.RotuloDoMotivoDeTeste;
		return (_recusas.Count > antes ? _recusas[^1] : "", selecao.Visible, World.Instancia != null, capaNoChao,
				r != null && r.IsVisibleInTree() && r.Text.Length > 0, r?.Text ?? "", r == null ? "" : r.Atr(r.Text),
				r != null && r.GetThemeColor("font_color").IsEqualApprox(Tema.Perigo));
	}

	/// <summary>O motivo na selecao: escrito, visivel, vermelho, igual a recusa que chegou, e a cobertura no chao.</summary>
	private static bool ReguaDoMotivoNaSelecao((string Recusa, bool Selecao, bool Mundo, bool CapaNoChao, bool Visivel, string Texto,
		string Desenhado, bool Vermelho) j) =>
		j.Selecao && j.CapaNoChao && j.Visivel && j.Vermelho && j.Recusa.Length > 0 && j.Texto == j.Recusa;

	private static string DescritoNaSelecao((string Recusa, bool Selecao, bool Mundo, bool CapaNoChao, bool Visivel, string Texto,
		string Desenhado, bool Vermelho) j) =>
		$"recusa '{j.Recusa}'; na selecao '{j.Texto}' (desenhado '{j.Desenhado}'); visivel {j.Visivel}, vermelho {j.Vermelho}; "
		+ $"selecao na tela {j.Selecao}, capa no chao {j.CapaNoChao}";

	// =====================================================================
	// AS TELAS DE PRODUCAO
	// =====================================================================
	/// <summary>O "Criar personagem" da selecao de producao (com o "Criar outro personagem" da pergunta do bebe, se ela abrir).</summary>
	private async System.Threading.Tasks.Task<CreationScreen?> AbrirComum(CharacterSelect selecao)
	{
		await Ate(() => selecao.Visible, 5);
		Apertar(Botao(selecao, "Criar personagem"));
		await Quadros(2);
		if (selecao.PerguntaDoBebeAbertaDeTeste) Apertar(selecao.BotaoDaPerguntaDeTeste("Criar outro personagem"));
		return await Achar<CreationScreen>(GetTree().Root, c => c.Visible, 8);
	}

	/// <summary>O "Criar personagem" e o "Nascer como filho(a)" da pergunta do bebe.</summary>
	private async System.Threading.Tasks.Task<CreationScreen?> AbrirComoFilho(CharacterSelect selecao)
	{
		await Ate(() => selecao.Visible, 5);
		Apertar(Botao(selecao, "Criar personagem"));
		await Ate(() => selecao.PerguntaDoBebeAbertaDeTeste, 3);
		Apertar(selecao.BotaoDaPerguntaDeTeste("Nascer como filho(a)"));
		return await Achar<CreationScreen>(GetTree().Root, c => c.Visible, 8);
	}

	/// <summary>
	/// O "Voltar" da criacao ate a primeira etapa -- e dela, de volta pra selecao (o `Cancelado`). Na primeira etapa o mesmo botao se
	/// chama "Cancelar".
	/// </summary>
	private async Tarefa VoltarPraSelecao(CreationScreen criacao)
	{
		for (int i = 0; i < 16 && criacao.Visible; i++)
		{
			Apertar(Botao(criacao, "Voltar") ?? Botao(criacao, "Cancelar"));
			await Segundos(0.2);
		}
	}

	/// <summary>
	/// Anda a criacao ate a etapa deste titulo. Com <paramref name="escolher"/>: a Terra, o Humano e uma peca de roupa quando passa por eles
	/// (os cartoes apertados de proposito, e nao o que a tela ja tinha: a medida de cada familia parte do zero).
	/// </summary>
	private async System.Threading.Tasks.Task<bool> IrAte(CreationScreen criacao, string titulo, bool escolher)
	{
		for (int i = 0; i < 20 && criacao.TituloDaEtapaDeTeste != titulo; i++)
		{
			string agora = criacao.TituloDaEtapaDeTeste;
			if (escolher && agora == "DE ONDE VEM O SEU POVO?") Apertar(Botao(criacao, "Terra"));
			if (escolher && agora == "ESCOLHA SUA RAÇA") Apertar(Botao(criacao, NomeDaRaca.Bonito("Human")));
			if (escolher && agora == "ROUPA" && criacao.RascunhoDeTeste.Pecas == 0) Apertar(PrimeiraPeca(criacao));
			await Quadros(2);
			Apertar(Botao(criacao, "Avançar"));
			await Segundos(0.25);
		}
		return criacao.TituloDaEtapaDeTeste == titulo;
	}

	/// <summary>O primeiro icone da grade de roupa (os botoes de 48 px da etapa ROUPA, a unica pagina visivel).</summary>
	private static Button? PrimeiraPeca(CreationScreen criacao) =>
		Todos<Button>(criacao).FirstOrDefault(b => b.IsVisibleInTree() && b.ToggleMode && b.CustomMinimumSize == new Vector2(48, 48));

	/// <summary>O nome (e a historia, se vier) na etapa "QUEM É VOCÊ?", como o jogador digitaria.</summary>
	private static void Escrever(CreationScreen criacao, string nome, string? historia)
	{
		if (Todos<LineEdit>(criacao).FirstOrDefault(l => l.PlaceholderText == "nome do personagem") is { } campo) campo.Text = nome;
		if (historia != null && Todos<TextEdit>(criacao).FirstOrDefault() is { } texto)
		{
			texto.Text = historia;
			texto.EmitSignal(TextEdit.SignalName.TextChanged);   // o `Text` escrito por codigo nao avisa
		}
	}

	/// <summary>
	/// PALCO: a criacao de volta a tela, se o defeito da familia anterior a deixou escondida (ou a selecao por cima dela) -- cada familia
	/// mede a partir da tela de pe, e uma falha nao cai em cascata nas seguintes.
	/// </summary>
	private async Tarefa DeVoltaATela(CreationScreen criacao, CharacterSelect selecao)
	{
		if (!criacao.Visible) criacao.ReabrirComOMotivo(_recusas.Count > 0 ? _recusas[^1] : "");
		selecao.Visible = false;
		await Quadros(3);
	}

	// =====================================================================
	// O QUE SE LE
	// =====================================================================
	/// <summary>O rotulo das recusas da criacao: na tela, o texto como o Godot o DESENHA (`Atr`, a traducao), vermelho, e a folga ate o botao.</summary>
	private static (bool Visivel, string Desenhado, bool Vermelho, float Folga) Rotulo(CreationScreen criacao)
	{
		Label r = criacao.RotuloDoErroDeTeste;
		Button? entrar = Botao(criacao, "Entrar no mundo");
		float folga = entrar == null ? -1 : entrar.GetGlobalRect().Position.Y - r.GetGlobalRect().End.Y;
		return (r.IsVisibleInTree() && r.Text.Length > 0, r.Atr(r.Text), r.GetThemeColor("font_color").IsEqualApprox(Tema.Perigo), folga);
	}

	/// <summary>(2026-10-24) A descricao da raca (o rodape da criacao) como o Godot a desenha.</summary>
	private static string DescricaoDesenhada(CreationScreen criacao)
	{
		Label d = criacao.RotuloDaDescricaoDeTeste;
		return d.Atr(d.Text);
	}

	private static string Descrito((bool Visivel, string Desenhado, bool Vermelho, float Folga) r, string texto) =>
		$"'{texto}' (desenhado '{r.Desenhado}'); visivel {r.Visivel}, vermelho {r.Vermelho}, {r.Folga:0} px acima do 'Entrar no mundo'";

	/// <summary>O mundo de fabrica (o primeiro), uma raca dele na ficha, e a tela marcando os dois.</summary>
	private static bool ReguaDoMundo((string Mundo, string MundoMarcado, string Raca, bool RacaDoMundo, string RacaMarcada) m) =>
		m.Mundo == CharacterDraft.Planetas[0] && m.MundoMarcado == "Terra" && m.RacaDoMundo && m.RacaMarcada == NomeDaRaca.Bonito(m.Raca);

	/// <summary>(2026-10-24) A idade de uma criacao comum: os 18 de sempre, destravada, sem bebe na ficha.</summary>
	private static bool ReguaDaIdade((int Idade, bool IdadeTravada, int FilhoDe, string Linhagem) f) =>
		f.Idade == 18 && !f.IdadeTravada && f.FilhoDe == 0;

	private static string Mundo((string Mundo, string MundoMarcado, string Raca, bool RacaDoMundo, string RacaMarcada) m) =>
		$"ficha {m.Raca} de {m.Mundo} (raca do mundo: {m.RacaDoMundo}); marcados na tela: mundo '{m.MundoMarcado}', raca '{m.RacaMarcada}'";

	private static IEnumerable<T> Todos<T>(Node raiz) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			// (o que o `QueueFree` ja condenou fica na arvore ate o fim do quadro -- a tela velha junto da nova)
			if (f.IsQueuedForDeletion()) continue;
			if (f is T t) yield return t;
			foreach (T n in Todos<T>(f)) yield return n;
		}
	}

	private static Button? Botao(Node? raiz, string texto) =>
		raiz == null ? null : Todos<Button>(raiz).FirstOrDefault(b => b.Text == texto && b.IsVisibleInTree());

	private static void Apertar(Button? b) => b?.EmitSignal(BaseButton.SignalName.Pressed);

	private async System.Threading.Tasks.Task<T?> Achar<T>(Node raiz, Func<T, bool> quer, double segundos) where T : Node
	{
		T? achado = null;
		await Ate(() => (achado = Todos<T>(raiz).FirstOrDefault(quer)) != null, segundos);
		return achado;
	}

	// =====================================================================
	// O TEMPO E A FOTO
	// =====================================================================
	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + s;
		while (Time.GetTicksMsec() / 1000.0 < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private void Fotografar(string nome)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
			img.SavePng(caminho);
			_fotos.Add(caminho);
		}
		catch (Exception e) { _linhas.Add("  --     sem foto: " + e.Message); }
	}

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print(Prefixo + l);
		foreach (string f in _fotos) GD.Print(Prefixo + "  foto " + f);
		GD.Print(_falhas.Count == 0 ? Prefixo + "===== TUDO OK =====" : $"{Prefixo}===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print(Prefixo + "  - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
