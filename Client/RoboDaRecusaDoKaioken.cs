using Godot;
using Catalogo = Jandirus.Core.Forms.Catalogo;
using KaiokenCore = Jandirus.Core.Skills.Kaioken;
using Servidor = Jandirus.Server.GameServer;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA RECUSA DO KAIO-KEN NA TELA (`--diagkaiokenveto`, 2026-10-21). O dono (2026-10-19): *"faca com q n de pra usar kaioken nas
/// formas de super saiyajin, somente no ssj blue pra quem e saiyajin de classe baixa e media"* -- o servidor ja recusa; a tela da escolha
/// (`TelaDeKaioken`) e o cartao da aba Forms passam a dizer a recusa ANTES do aperto, com a frase do servidor. Com janela e `--host`, um
/// Saiyajin; o palco (`GameServer.KaiokenNaTelaPalco.cs`) poe a classe, a maestria divina e a forma de cada caso, e cada caso e conferido
/// contra o SERVIDOR: a tela recusa exatamente quando ele recusa, com a mesma frase -- e acende pelo botao quando ele aceita.
///
/// O VETO ESPERADO DE CADA CASO ESTA ESCRITO AQUI (o do dono), e nao perguntado a regra: uma regua que perguntasse a producao aceitaria
/// qualquer resposta. A frase vem de `Kaioken.FraseDoVeto`, que e a que o servidor manda no chat -- e o R*s confere que ele a mandou.
///
///   R1. BASE, classe baixa a 50%: nada muda -- sem linha vermelha, o botao "Acender" ligado, e ele acende no servidor. Foto `kveto-1-base`.
///   R2. SUPER SAIYAJIN: a frase do servidor em vermelho acima dos botoes e o botao desligado; o servidor recusa o mesmo botao com a mesma
///       frase -- foto `kveto-2-ssj1`, com o chat dele embaixo. CONTRA-EXEMPLO `RecusaCegaDeTeste`. E o segundo aperto da TECLA nao acende: a
///       tela fica aberta e o servidor apagado. CONTRA-EXEMPLO `AcendeComRecusaDeTeste`.
///   R3. SUPER SAIYAJIN 2: a mesma recusa, com o nome do SSJ2 -- foto `kveto-3-ssj2`; e o cartao do Kaio-ken na aba Forms com a mesma frase
///       -- foto `kveto-4-aba-ssj2`.
///   R4. BLUE de classe baixa a 50%: nada muda, e o botao acende no servidor -- foto `kveto-5-blue-baixa`. O cartao, sem a linha.
///   R5. BLUE de Elite a 100%: a recusa da classe -- foto `kveto-6-blue-elite`.
///   R6. BLUE de classe baixa a 40%: a recusa da maestria -- foto `kveto-7-blue-40`. Com a tela ABERTA a maestria vai a 50% (so o
///       `S2C.GodKi`, sem a ficha lenta) e a recusa some. CONTRA-EXEMPLO `SurdaAoKiDivinoDeTeste`. O mesmo no cartao da aba Forms aberta --
///       foto `kveto-8-aba-blue-40`. CONTRA-EXEMPLO `MenuJogo.RecusaForaDaAssinaturaDeTeste`.
///   R7. MISTICO (2026-10-21, *"kaioken n poderia ser usado com NENHUMA transformacao, o blue e a unica excecao a regra"*): uma
///       transformacao que nao e Super Saiyajin recusa com a frase dela, no servidor e na tela -- foto `kveto-9-mistico`.
///
///     Godot --janela --resolution 1280x720 --host --rede 10301 --campoteste 8 --horateste 0.5 --semfauna --diagkaiokenveto --raca Saiyan --conta bancada_kveto --nome Recusa
/// </summary>
public partial class RoboDaRecusaDoKaioken : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private int _ok;

	private const string Baixa = "Low-Class";
	private const string Botao = "Não acende nesta forma";

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		_linhas.Add((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? "   " + detalhe : ""));
		if (passou) _ok++;
		else _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree() => DesligarOsDefeitos();

	private static void DesligarOsDefeitos()
	{
		TelaDeKaioken.RecusaCegaDeTeste = TelaDeKaioken.AcendeComRecusaDeTeste = TelaDeKaioken.SurdaAoKiDivinoDeTeste = false;
		MenuJogo.RecusaForaDaAssinaturaDeTeste = false;
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Checa("o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}"); GD.Print(e.ToString()); }
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => GameClient.Instance is { Connected: true, LocalId: > 0 } && Servidor.Instance != null
									  && TelaDeKaioken.Instancia != null && MenuJogo.Instancia != null && World.Instancia != null, 90);
		Checa("(montagem) em 90 s o cliente conectou, o servidor e deste processo e a tela do Kaio-ken e o menu P nasceram", pronto);
		if (!pronto || GameClient.Instance is not { } cli || Servidor.Instance is not { } srv
			|| TelaDeKaioken.Instancia is not { } tela || MenuJogo.Instancia is not { } menu)
			return;
		await Segundos(1.5);
		GD.Print("[dkveto] ===== A RECUSA DO KAIO-KEN NA TELA, ANTES DO APERTO (2026-10-21) =====");

		bool sabe = srv.PalcoV30_AprenderOKaioken(cli.LocalId)
					&& await Ate(() => PosseDoCliente.Atual.SabeTecnica(TelaDeKaioken.VerboDoKaioken), 5);
		Checa("(montagem) o corpo aprendeu o Kaio-ken pela licao do Sr. Kaioh, e o cliente sabe", sabe);
		if (!sabe) return;
		try
		{
			await R1_ABase(cli, srv, tela);
			await R2_OSuperSaiyajin(cli, srv, tela);
			await R3_OSuperSaiyajin2(cli, srv, tela, menu);
			await R4_OBlueDaClasseBaixa(cli, srv, tela, menu);
			await R5_OBlueDoElite(cli, srv, tela);
			await R6_AMaestriaDivina(cli, srv, tela, menu);
			await R7_OMistico(cli, srv, tela);
		}
		finally
		{
			DesligarOsDefeitos();
			tela.Fechar();
			menu.Fechar();
			srv.PalcoV30_ApagarOKaioken(cli.LocalId);
			srv.PalcoL_Forma(cli.LocalId, Catalogo.IdBase);
		}
	}

	// =====================================================================
	// R1) A BASE: NADA MUDA
	// =====================================================================
	private async Tarefa R1_ABase(GameClient cli, Servidor srv, TelaDeKaioken tela)
	{
		if (!await Montar(cli, srv, Baixa, 50, Catalogo.IdBase)) return;
		await Livre(cli, srv, tela, "R1", "na base", "kveto-1-base");
	}

	// =====================================================================
	// R2) O SUPER SAIYAJIN: A RECUSA, O SERVIDOR E A TECLA
	// =====================================================================
	private async Tarefa R2_OSuperSaiyajin(GameClient cli, Servidor srv, TelaDeKaioken tela)
	{
		if (!await Montar(cli, srv, Baixa, 50, Catalogo.IdSsj1)) return;
		await Recusada(cli, srv, tela, "R2", Catalogo.IdSsj1, KaiokenCore.Veto.SuperSaiyajin, "no Super Saiyajin", "kveto-2-ssj1");

		TelaDeKaioken.RecusaCegaDeTeste = true;
		try
		{
			var cega = await AbrirELer(tela);
			Checa("CONTRA-EXEMPLO (defeito injetado: a tela que nao ve a recusa -- o jogo de antes): no Super Saiyajin ela volta a oferecer "
				  + "\"Acender\", sem linha vermelha -- a regua do R2 REPROVA", !cega.Visivel && !cega.Desligado && cega.Botao.StartsWith("Acender"),
				  $"[\"{cega.Botao}\", linha a vista={cega.Visivel}]");
		}
		finally { TelaDeKaioken.RecusaCegaDeTeste = false; }

		// O SEGUNDO APERTO DA TECLA (`Pedir` com a tela aberta) e o mesmo botao: desligado, nao manda nada.
		var aberta = await AbrirELer(tela);
		tela.Pedir(TelaDeKaioken.VerboDoKaioken);
		await Segundos(0.6);
		bool ficou = tela.NaTela, aceso = srv.PalcoV30_Kaioken(cli.LocalId).Aceso;
		Checa("R2t. **O SEGUNDO APERTO DA TECLA NAO ACENDE**: com a recusa na tela, a tecla do Kaio-ken nao manda o verbo -- a tela fica "
			  + "aberta, com o porque, e o servidor apagado", aberta.Desligado && ficou && !aceso, $"[tela aberta={ficou} servidor aceso={aceso}]");

		AbrirDeNovo(tela);
		await Quadros(2);
		TelaDeKaioken.AcendeComRecusaDeTeste = true;
		try
		{
			tela.Pedir(TelaDeKaioken.VerboDoKaioken);
			await Segundos(0.6);
			Checa("CONTRA-EXEMPLO (defeito injetado: o segundo aperto passa pelo botao desligado): o verbo sai, o servidor recusa no chat e a "
				  + "tela se fecha levando o porque -- a regua do R2t REPROVA", !tela.NaTela, $"[tela aberta={tela.NaTela}]");
		}
		finally { TelaDeKaioken.AcendeComRecusaDeTeste = false; }
		tela.Fechar();
	}

	// =====================================================================
	// R3) O SUPER SAIYAJIN 2, NA TELA E NO CARTAO DA ABA FORMS
	// =====================================================================
	private async Tarefa R3_OSuperSaiyajin2(GameClient cli, Servidor srv, TelaDeKaioken tela, MenuJogo menu)
	{
		if (!await Montar(cli, srv, Baixa, 50, "ssj2")) return;
		await Recusada(cli, srv, tela, "R3", "ssj2", KaiokenCore.Veto.SuperSaiyajin, "no Super Saiyajin 2", "kveto-3-ssj2");
		tela.Fechar();

		string esperada = Esperada(KaiokenCore.Veto.SuperSaiyajin, "ssj2");
		await AbrirAAbaForms(menu);
		string linha = LinhaDaRecusaNoCartao(menu);
		Checa("R3c. **O CARTAO DO KAIO-KEN NA ABA FORMS DIZ A MESMA RECUSA**, em vermelho, sem abrir a escolha",
			  linha == esperada && CorDaRecusaNoCartao(menu) == Tema.Perigo, $"[\"{linha}\"]");
		await Fotografar("kveto-4-aba-ssj2", CartaoDoKaioken(menu));

		TelaDeKaioken.RecusaCegaDeTeste = true;
		try
		{
			menu.ForcarRedesenho();
			await Quadros(2);
			Checa("CONTRA-EXEMPLO (defeito injetado: a recusa cega): o cartao sai sem a linha -- a regua do R3c REPROVA",
				  LinhaDaRecusaNoCartao(menu) == "", $"[\"{LinhaDaRecusaNoCartao(menu)}\"]");
		}
		finally { TelaDeKaioken.RecusaCegaDeTeste = false; }
		menu.ForcarRedesenho();
		await Quadros(2);
		menu.Fechar();
	}

	// =====================================================================
	// R4) O BLUE DA CLASSE BAIXA A 50%: NADA MUDA
	// =====================================================================
	private async Tarefa R4_OBlueDaClasseBaixa(GameClient cli, Servidor srv, TelaDeKaioken tela, MenuJogo menu)
	{
		if (!await Montar(cli, srv, Baixa, 50, Catalogo.IdBlue)) return;
		await Livre(cli, srv, tela, "R4", "no Blue da classe baixa com 50% de maestria divina", "kveto-5-blue-baixa");

		await AbrirAAbaForms(menu);
		Checa("R4c. ...e o cartao do Kaio-ken na aba Forms sai sem a linha da recusa",
			  CartaoDoKaioken(menu) != null && LinhaDaRecusaNoCartao(menu) == "", $"[\"{LinhaDaRecusaNoCartao(menu)}\"]");
		menu.Fechar();
	}

	// =====================================================================
	// R5) O BLUE DO ELITE: A CLASSE
	// =====================================================================
	private async Tarefa R5_OBlueDoElite(GameClient cli, Servidor srv, TelaDeKaioken tela)
	{
		if (!await Montar(cli, srv, Catalogo.ClasseElite, 100, Catalogo.IdBlue)) return;
		await Recusada(cli, srv, tela, "R5", Catalogo.IdBlue, KaiokenCore.Veto.ClasseNoBlue, "no Blue de um Elite com 100% de maestria divina",
					   "kveto-6-blue-elite");
		tela.Fechar();
	}

	// =====================================================================
	// R6) O BLUE A 40% E A MAESTRIA QUE CHEGA COM A TELA ABERTA
	// =====================================================================
	private async Tarefa R6_AMaestriaDivina(GameClient cli, Servidor srv, TelaDeKaioken tela, MenuJogo menu)
	{
		if (!await Montar(cli, srv, Baixa, 40, Catalogo.IdBlue)) return;
		await Recusada(cli, srv, tela, "R6", Catalogo.IdBlue, KaiokenCore.Veto.MaestriaNoBlue, "no Blue da classe baixa com 40% de maestria divina",
					   "kveto-7-blue-40");

		// ---- a tela aberta ----
		bool chegou = await MaestriaDivina(cli, srv, 50);
		var r = tela.RecusaDeTeste();
		Checa("R6v. **A TELA ABERTA ACOMPANHA O KI DIVINO**: a maestria cruza os 50% (so o `S2C.GodKi`) e a recusa some, com o botao ligado, "
			  + "sem fechar a tela", chegou && tela.NaTela && !r.Visivel && !r.Desligado, $"[aberta={tela.NaTela} linha={r.Visivel} desligado={r.Desligado}]");
		bool voltou = await MaestriaDivina(cli, srv, 40) && tela.RecusaDeTeste().Visivel;
		Checa("(montagem) a 40% de novo, a recusa volta na tela aberta", voltou);
		TelaDeKaioken.SurdaAoKiDivinoDeTeste = true;
		try
		{
			bool surda = await MaestriaDivina(cli, srv, 50);
			Checa("CONTRA-EXEMPLO (defeito injetado: a tela surda ao `S2C.GodKi`): a 50% a recusa continua na tela -- a regua do R6v REPROVA",
				  surda && tela.RecusaDeTeste().Visivel, $"[linha={tela.RecusaDeTeste().Visivel}]");
		}
		finally { TelaDeKaioken.SurdaAoKiDivinoDeTeste = false; }
		tela.Fechar();

		// ---- o cartao da aba Forms aberta ----
		await MaestriaDivina(cli, srv, 40);
		await AbrirAAbaForms(menu);
		string esperada = Esperada(KaiokenCore.Veto.MaestriaNoBlue, Catalogo.IdBlue);
		Checa("R6c. o cartao do Kaio-ken diz a recusa da maestria", LinhaDaRecusaNoCartao(menu) == esperada, $"[\"{LinhaDaRecusaNoCartao(menu)}\"]");
		await Fotografar("kveto-8-aba-blue-40", CartaoDoKaioken(menu));
		bool cruzou = await MaestriaDivina(cli, srv, 50);
		await Quadros(3);
		Checa("R6a. **O CARTAO DA ABA ABERTA ACOMPANHA O KI DIVINO**: a 50% a linha da recusa sai sem ninguem tocar na aba",
			  cruzou && LinhaDaRecusaNoCartao(menu) == "", $"[\"{LinhaDaRecusaNoCartao(menu)}\"]");

		await MaestriaDivina(cli, srv, 40);
		await Quadros(3);
		Checa("(montagem) a 40% de novo, a linha volta no cartao", LinhaDaRecusaNoCartao(menu) == esperada);
		MenuJogo.RecusaForaDaAssinaturaDeTeste = true;
		try
		{
			// O defeito entra NA assinatura: a remontagem que ele mesmo provocaria (a assinatura nova sem a recusa) e feita antes da medida.
			menu.ForcarRedesenho();
			await Quadros(2);
			int antes = menu.RemontagensDaAbaDeTeste("Forms");
			bool foi = await MaestriaDivina(cli, srv, 50);
			await Quadros(3);
			int remontou = menu.RemontagensDaAbaDeTeste("Forms") - antes;
			Checa("CONTRA-EXEMPLO (defeito injetado: a recusa fora da assinatura da aba): a 50% o cartao continua dizendo a recusa -- a regua do "
				  + "R6a REPROVA", foi && LinhaDaRecusaNoCartao(menu) == esperada, $"[remontagens no meio={remontou}, \"{LinhaDaRecusaNoCartao(menu)}\"]");
		}
		finally { MenuJogo.RecusaForaDaAssinaturaDeTeste = false; }
		menu.Fechar();
	}

	// =====================================================================
	// R7) O MISTICO: NENHUMA TRANSFORMACAO (2026-10-21)
	// =====================================================================
	private async Tarefa R7_OMistico(GameClient cli, Servidor srv, TelaDeKaioken tela)
	{
		if (!await Montar(cli, srv, Baixa, 50, Catalogo.IdMistico)) return;
		await Recusada(cli, srv, tela, "R7", Catalogo.IdMistico, KaiokenCore.Veto.Transformacao, "no Mistico (uma transformacao que nao e Super Saiyajin)",
					   "kveto-9-mistico");
		tela.Fechar();
	}

	// =====================================================================
	// OS DOIS JEITOS DE UM CASO
	// =====================================================================
	/// <summary>NADA MUDA: sem linha vermelha, o botao de sempre ligado, e ele acende no servidor (e apaga logo).</summary>
	private async Tarefa Livre(GameClient cli, Servidor srv, TelaDeKaioken tela, string cod, string onde, string foto)
	{
		var t = await AbrirELer(tela);
		int x = tela.TextosDeTeste().Escolhido;
		Checa($"{cod}. **NADA MUDA {onde.ToUpperInvariant()}**: sem linha vermelha, e o botao de sempre (\"Acender o Kaio-ken x{x}\") ligado",
			  !t.Visivel && t.Texto == "" && !t.Desligado && t.Botao == $"Acender o Kaio-ken x{x}",
			  $"[\"{t.Botao}\" desligado={t.Desligado} linha=\"{t.Texto}\"]");
		await Fotografar(foto, null);
		tela.ApertarDeTeste();
		bool acendeu = await Ate(() => srv.PalcoV30_Kaioken(cli.LocalId).Aceso, 5);
		Checa($"{cod}s. ...e o botao acende o Kaio-ken no servidor", acendeu, $"[servidor={srv.PalcoV30_Kaioken(cli.LocalId)}]");
		srv.PalcoV30_ApagarOKaioken(cli.LocalId);
		await Ate(() => !cli.Atributos.KaiokenAceso, 5);
		tela.Fechar();
	}

	/// <summary>
	/// A RECUSA: o servidor recusa o botao primeiro (a frase dele fica no chat, embaixo da foto), e a tela tem de dizer a MESMA frase em
	/// vermelho, com o botao desligado.
	/// </summary>
	private async Tarefa Recusada(GameClient cli, Servidor srv, TelaDeKaioken tela, string cod, string forma, KaiokenCore.Veto veto, string onde,
								  string foto)
	{
		string frase = KaiokenCore.FraseDoVeto(veto, Catalogo.Def(forma));
		(bool acendeu, string falas) = srv.PalcoKV_Apertar(cli.LocalId, "Kaioken_5");
		Checa($"{cod}s. (o servidor) {onde}, o botao do Kaio-ken e recusado com a frase do veto {veto}",
			  !acendeu && falas.Contains(frase, StringComparison.Ordinal), $"[acendeu={acendeu} disse \"{falas}\"]");
		await Segundos(0.3);

		var t = await AbrirELer(tela);
		Checa($"{cod}. **A TELA DIZ A RECUSA ANTES DO APERTO** {onde}: a frase do servidor em vermelho acima dos botoes, e o botao "
			  + $"\"{Botao}\" desligado", t.Visivel && t.Texto == Esperada(veto, forma) && t.Desligado && t.Botao == Botao,
			  $"[\"{t.Texto}\" a vista={t.Visivel} | \"{t.Botao}\" desligado={t.Desligado}]");
		await Fotografar(foto, null);
	}

	/// <summary>A frase do servidor como a tela a escreve: a mesma, com a primeira letra maiuscula (ela abre a linha).</summary>
	private static string Esperada(KaiokenCore.Veto veto, string forma)
	{
		string frase = KaiokenCore.FraseDoVeto(veto, Catalogo.Def(forma));
		return char.ToUpperInvariant(frase[0]) + frase[1..];
	}

	// =====================================================================
	// O PALCO
	// =====================================================================
	/// <summary>A classe, a maestria divina e a forma no servidor, e espera o cliente ver as tres (a ficha lenta e o `S2C.GodKi`).</summary>
	private async System.Threading.Tasks.Task<bool> Montar(GameClient cli, Servidor srv, string classe, double maestria, string forma)
	{
		srv.PalcoV30_ApagarOKaioken(cli.LocalId);
		srv.PalcoKV_Corpo(cli.LocalId, classe, maestria);
		string ficou = srv.PalcoL_Forma(cli.LocalId, forma);
		ushort rede = Catalogo.Rede(forma);
		bool chegou = await Ate(() => cli.Atributos.FormaAtual == rede && cli.Atributos.Classe == classe && cli.KiDivino.Desperto
									  && Math.Abs(cli.KiDivino.Maestria - maestria) < 0.5 && !cli.Atributos.KaiokenAceso, 8);
		Checa($"(montagem) {classe}, {maestria:0}% de maestria divina, em {forma}: o servidor e o cliente concordam", chegou && ficou == forma,
			  $"[servidor: {ficou} | cliente: forma {cli.Atributos.FormaAtual} (esperada {rede}), classe {cli.Atributos.Classe}, maestria divina "
			  + $"{cli.KiDivino.Maestria:0.0}]");
		await Segundos(0.4);   // a aura e o cabelo da forma assentam pra foto
		return chegou && ficou == forma;
	}

	/// <summary>So a maestria divina (sem a ficha lenta), e espera o `S2C.GodKi` chegar.</summary>
	private async System.Threading.Tasks.Task<bool> MaestriaDivina(GameClient cli, Servidor srv, double maestria)
	{
		srv.PalcoKV_MaestriaDivina(cli.LocalId, maestria);
		bool chegou = await Ate(() => Math.Abs(cli.KiDivino.Maestria - maestria) < 0.5, 5);
		await Quadros(2);
		return chegou;
	}

	// =====================================================================
	// LER A TELA
	// =====================================================================
	/// <summary>Abre a escolha pelo gesto do cartao (`Kaioken_Settings`) e le a recusa e o botao.</summary>
	private async System.Threading.Tasks.Task<(string Texto, bool Visivel, bool Desligado, string Botao)> AbrirELer(TelaDeKaioken tela)
	{
		AbrirDeNovo(tela);
		await Quadros(2);
		var r = tela.RecusaDeTeste();
		return (r.Texto, r.Visivel, r.Desligado, tela.TextosDeTeste().Botao);
	}

	private static void AbrirDeNovo(TelaDeKaioken tela)
	{
		tela.Fechar();
		tela.Pedir("Kaioken_Settings");
	}

	private async Tarefa AbrirAAbaForms(MenuJogo menu)
	{
		menu.Abrir();
		menu.IrPara("Forms");
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(2);
	}

	/// <summary>O cartao "Kaio-ken" da aba Forms (o `Cartao` marca o titulo no metadado).</summary>
	private static PanelContainer? CartaoDoKaioken(MenuJogo menu) =>
		menu.PaginaDeTeste("Forms") is { } pg
			? Todos(pg).OfType<PanelContainer>().FirstOrDefault(c => c.HasMeta("titulo") && c.GetMeta("titulo").AsString() == "Kaio-ken"
																	  && c.IsVisibleInTree())
			: null;

	/// <summary>O rotulo "nesta forma" do cartao (a `LinhaDeTextoLongo`) e o texto ao lado dele; nenhum = vazio.</summary>
	private static Label? RotuloDaRecusa(MenuJogo menu)
	{
		if (CartaoDoKaioken(menu) is not { } c) return null;
		foreach (HBoxContainer h in Todos(c).OfType<HBoxContainer>())
			if (h.GetChildCount() == 2 && h.GetChild(0) is Label { Text: "nesta forma" } && h.GetChild(1) is Label texto) return texto;
		return null;
	}

	private static string LinhaDaRecusaNoCartao(MenuJogo menu) => RotuloDaRecusa(menu)?.Text ?? "";

	private static Color? CorDaRecusaNoCartao(MenuJogo menu) => RotuloDaRecusa(menu)?.GetThemeColor("font_color");

	/// <summary>A arvore inteira, SEM os nos que ja vao embora (a pagina velha fica na arvore ate o fim do quadro da remontagem).</summary>
	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f.IsQueuedForDeletion()) continue;
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	// =====================================================================
	// A FOTO E O RELOGIO
	// =====================================================================
	/// <summary>A foto da tela inteira; com um cartao, rolado ate ele ficar a vista.</summary>
	private async Tarefa Fotografar(string nome, Control? aVista)
	{
		if (aVista != null && Ancestral<ScrollContainer>(aVista) is { } rol) rol.EnsureControlVisible(aVista);
		await Quadros(3);
		await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto `{nome}` (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota("foto salva em " + caminho);
	}

	private static T? Ancestral<T>(Node n) where T : Node
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is T t) return t;
		return null;
	}

	private static double Agora => Time.GetTicksMsec() / 1000.0;

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	private async Tarefa Segundos(double s)
	{
		double fim = Agora + s;
		while (Agora < fim) await Quadros(1);
	}

	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Agora + segundos;
		while (Agora < fim)
		{
			if (cond()) return true;
			await Quadros(1);
		}
		return cond();
	}

	private void Fechar()
	{
		DesligarOsDefeitos();
		foreach (string l in _linhas) GD.Print("[dkveto] " + l);
		GD.Print($"[dkveto] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0 ? "[dkveto] ===== TUDO OK =====" : $"[dkveto] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dkveto]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
