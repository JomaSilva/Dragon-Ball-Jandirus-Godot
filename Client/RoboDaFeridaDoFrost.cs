using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA FERIDA NO CORPO DO FROST DEMON (`--diagferidafrost`, 2026-10-19). A ferida (o roxo e o sangue) e o membro decepado sao pintados
/// pelo shader nas camadas que os recebem (`CharacterVisual.Ferir`), e ate aqui so o corpo da BASE e a roupa recebiam -- mas o Frost Demon
/// veste em TODA forma um corpo inteiro proprio (a camada de corpo da forma, ordem 1), e o da base some debaixo dele
/// (`Catalogo.CorpoDaFormaEscondeOBase`). Um Frost Demon nunca mostrava ferida nem membro faltando. Com janela e `--host`, nascendo Humano:
///   F0. a premissa (`GameServer.VirarFrostDeTeste`, o gancho do `--diagcorpofrost`): Frost Demon NORMAL descansando na forma base, inteiro,
///       e a luz do palco medida nele -- foto `feridafrost-0-repouso.png`;
///   F1. o C: a 1a Evolucao, AINDA INTEIRA -- o controle: a regua acha os dois bracos e nao ve ferida onde nao ha -- `feridafrost-1-inteira`;
///   F2. o corpo apanha com a evolucao vestida (`GameServer.FerirParaFotoDeTeste`: 30% de vida em tudo e o BRACO DIREITO decepado): a ferida
///       aparece no corpo da evolucao e o braco direito some -- de frente, no lado ESQUERDO da imagem -- `feridafrost-2-ferida`;
///   F3. vira de COSTAS: o braco que falta troca de lado na imagem (o DIREITO dela) -- `feridafrost-3-de-costas`;
///   F4. vira de frente e o X: o REPOUSO -- o X devolve o braco (a Revitalizing Metamorphosis do Frost), o corpo apanha de novo, e a
///       camada reciclada da evolucao, agora com a folha do repouso, mostra a ferida e o braco faltando -- `feridafrost-4-repouso`;
///   F5. CONTRA-EXEMPLO (`CharacterVisual.FeridaSoNoCorpoDeTeste`, a regra de antes): a camada da forma RENASCE com o corpo ja ferido -- o
///       que acontece com quem entra na zona e ve este Frost, porque o `World.VestirCorpoInteiro` pinta a ferida ANTES de vestir a forma --
///       e nasce LIMPA e com o braco, e a regua de F4 REPROVA -- `feridafrost-5-defeito-limpo`;
///   F6. a camada renasce de novo, sem o defeito: ja nasce ferida e sem o braco -- `feridafrost-6-renascida`;
///   F7. (o PEDACO SOLTO, dono, 2026-10-19: "o pedaco some junto") o braco ESQUERDO tambem e decepado -- de frente ele cai do lado por
///       onde sobe o rabo do "Changeling 1 Large", e o corte por faixa do shader separava a ponta do rabo do corpo. Nenhum pedaco fica
///       solto na tela -- `feridafrost-7-sem-os-dois-bracos`;
///   F8. CONTRA-EXEMPLO (`CharacterVisual.PedacoSoltoFicaDeTeste`, o corte de antes): a camada renasce e a ponta do rabo aparece
///       FLUTUANDO, e a regua de F7 REPROVA -- `feridafrost-8-defeito-pedaco-solto`;
///   F9. a camada renasce de novo, sem o defeito, ja sem o pedaco solto -- `feridafrost-9-renascida-sem-solto`.
///
/// ============================ A REGUA E DE PIXEL, COM A FOTO DO CHAO ============================
/// Cada cena tira DUAS fotos seguidas: a do palco e a do CHAO, com o boneco escondido por tres quadros (`-chao.png`). Um pixel do quadro
/// que a camada da forma desenha ESTA NA TELA quando a foto, naquele ponto, difere da do chao -- vale pro contorno escuro tambem, e nao
/// depende da luz. Na faixa que o shader corta como BRACO (<see cref="ZonaDoBraco"/>, longe das divisas), a regua conta quantos pixels de
/// cada lado estao na tela; no MIOLO da cabeca e do tronco, quantos dos que estao na tela SAIRAM DA COR do quadro sob a luz medida em F0 --
/// e a ferida. A conta de papel nas folhas (o quadro parado `walk_south#0` e o `walk_north#0`) da de 15 a 64 pixels por lado na faixa do
/// braco e 112 no miolo, nos dois corpos.
///
/// As camadas sao lidas da ARVORE (os filhos do `CharacterVisual` ja carregam folha, animacao, quadro e a marca de `ordem`), como faz o
/// `RoboDoCorpoDoFrost`, sem API nova no `CharacterVisual` alem do botao de defeito.
/// ==============================================================================================
///
///     Godot --path . --host --rede 9161 --campoteste 8 --horateste 0.5 --semfauna --diagferidafrost --raca Human --conta bancada_feridafrost --nome Ferido
/// </summary>
public partial class RoboDaFeridaDoFrost : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>A base, a 1a e a 2a Evolucao -- as do `--diagcorpofrost` (a 2a nao e visitada; a classe normal pede os tres corpos).</summary>
	private static readonly string[] Corpos = ["Changeling 1 Large", "Changeling Full Power", "Koola Final Form"];

	/// <summary>BP de sobra pra porta da 1a Evolucao (`FormasDeFrost.BpNecessario`).</summary>
	private const double Bp = 2e10;

	/// <summary>O quanto a cor da tela pode fugir da do quadro sob a luz do palco, por canal (0 a 255), e ainda ser o mesmo pixel.</summary>
	private const int Folga = 40;

	/// <summary>
	/// O quanto a foto pode fugir da foto do CHAO no mesmo ponto e ainda ser chao, por canal: as duas saem com tres quadros de distancia, e
	/// so o que se mexe sozinho (a grama, uma folha) as separa.
	/// </summary>
	private const int FolgaDoChao = 30;

	/// <summary>Um pixel do quadro e de COR VIVA quando o canal mais forte passa disto -- so esses entram na conta da COR (a ferida).</summary>
	private const int Viva = 80;

	/// <summary>
	/// OS LIMIARES: um braco esta na tela com ao menos <see cref="BracoNaTela"/> dos pixels da faixa dele, e fora dela com no maximo
	/// <see cref="BracoFora"/>; o corpo esta FERIDO com ao menos <see cref="Ferido"/> do miolo fora da cor do quadro, e LIMPO com no maximo
	/// <see cref="Limpo"/>; e o corpo inteiro esta na tela com <see cref="MioloNaTela"/> do miolo.
	///
	/// O FERIDO E BAIXO DE PROPOSITO: o roxo do shader e uma mancha de ruido que mora no pixel do quadro e sai do SORTEIO do corpo (a
	/// `ferida_semente`, do id), e o miolo tem so 66 a 78 pixels. A conta de papel do shader com a mascara do robo deu de 21% a 94% do miolo
	/// fora da cor (mediana 65%) nos ids 1 a 40; o corpo limpo da rodada de antes do conserto, 0 a 3%.
	/// </summary>
	private const double BracoNaTela = 0.60, BracoFora = 0.20, Ferido = 0.15, Limpo = 0.10, MioloNaTela = 0.80;

	/// <summary>O menor numero de pixels por faixa que deixa a cena dizer alguma coisa.</summary>
	private const int FaixaMinima = 8, MioloMinimo = 30;

	/// <summary>
	/// O PEDACO SOLTO NA TELA (<see cref="ContarSoltos"/>): ate quanto ainda e "nenhum", e quanto o defeito tem de mostrar. A conta de papel
	/// nas folhas da o "Changeling 1 Large" de frente, sem os dois bracos, com 28 pixels soltos pelo corte de hoje (a ponta do rabo, 24, e um
	/// risco do ombro, 4) e 0 com o pedaco solto sumindo junto.
	/// </summary>
	private const int SoltoTolerado = 3, SoltoNoDefeito = 10;

	private int _eu;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string texto) => _linhas.Add("  --     " + texto);

	public override void _Ready() => _ = Rodar();

	/// <summary>Os defeitos injetados nunca sobrevivem ao robo, nem se ele cair no meio.</summary>
	public override void _ExitTree() => Desligar();

	private static void Desligar()
	{
		CharacterVisual.FeridaSoNoCorpoDeTeste = false;
		CharacterVisual.PedacoSoltoFicaDeTeste = false;
	}

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		Desligar();
		Fechar();
	}

	private static CharacterVisual? VisualDe(int id) => World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>
	/// O C (ou o X) pelo fio e a espera: o servidor na forma pedida, sem cena, a camada vestindo a folha pedida (a descida pro repouso nao
	/// pede: a folha dele pode vir do cache sem caminho) e o estalo da troca passado.
	/// </summary>
	private async System.Threading.Tasks.Task<bool> Subir(bool subir, string forma, string? folha)
	{
		C?.SendTransformar(subir);
		bool chegou = await Ate(() => S?.FormaDoCorpoDeTeste(_eu) == forma && S?.EmCenaDeTeste(_eu) == false
										  && (folha == null || VisualDe(_eu)?.FolhaDoCorpoDaFormaDeTeste == folha), 10);
		await Segundos(1.5);
		return chegou;
	}

	/// <summary>
	/// O corpo apanha (`GameServer.FerirParaFotoDeTeste`) e a espera: a mascara com o braco DIREITO decepado chegou ao cliente pelo fio
	/// (`S2C.Feridas`, a 5 Hz) -- e com ela o `Ferir` de producao rodou no boneco.
	/// </summary>
	private async System.Threading.Tasks.Task<bool> Apanhar()
	{
		bool chegou = S?.FerirParaFotoDeTeste(_eu) == true
					  && await Ate(() => C?.Feridas.TryGetValue(_eu, out MascaraDeFeridas m) == true
										 && m.Perdeu(MascaraDeFeridas.Membro.BracoDir), 5);
		await Segundos(1);
		return chegou;
	}

	/// <summary>
	/// Vira o MEU corpo pelo mesmo canal com que o servidor crava o olhar (`LocalPlayer.ReceberOlhar`) e espera a camada da forma estar
	/// desenhando a direcao nova -- e a virada que chama o `SeguirDirecaoNaAmputacao`.
	/// </summary>
	private async System.Threading.Tasks.Task<bool> Virar(Jandirus.Core.World.Facing olhar, string sufixo)
	{
		if (World.Instancia?.CorpoDeTeste(_eu) is not LocalPlayer eu) return false;
		eu.ReceberOlhar(olhar);
		bool virou = await Ate(() => VisualDe(_eu) is { } v && CamadaDaForma(v) is { } f && f.Animation.ToString().EndsWith(sufixo), 5);
		await Segundos(1);
		return virou;
	}

	/// <summary>
	/// A CAMADA DA FORMA RENASCE: tirada e vestida de novo pelo metodo de producao (`CharacterVisual.CorpoDaForma`), com a mascara de feridas
	/// ja guardada no boneco -- o caminho de quem ve este corpo pela primeira vez (`World.VestirCorpoInteiro`: o `Ferir` antes do
	/// `VestirAFormaSemCena`).
	/// </summary>
	private async System.Threading.Tasks.Task<bool> RenascerACamada(string forma)
	{
		if (VisualDe(_eu) is not { } v || Catalogo.Def(forma) is not { } d) return false;
		v.CorpoDaForma(CorpoDeForma.Nenhum);
		bool sumiu = !v.CorpoDaFormaDeTeste;
		v.CorpoDaForma(d);
		await Segundos(1);
		return sumiu && v.CorpoDaFormaDeTeste;
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = _eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dferidafrost] ===== A FERIDA NO CORPO DA FORMA DO FROST DEMON (2026-10-19) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);

		string folhaBase = FormasDeFrost.Caminho(Corpos[0]);
		string base5 = Catalogo.IdDaBaseDoFrost;
		string evo1 = Catalogo.Todas.First(d => Catalogo.DegrauDoFrost(d) == FormasDeFrost.Evolucao).Id;

		// ---------------------------------------------------------- F0. a premissa
		bool virou = srv.VirarFrostDeTeste(eu, Corpos, Bp);
		bool vestiu = virou && await Ate(() => srv.FormaDoCorpoDeTeste(eu) == base5 && VisualDe(eu)?.FolhaDoCorpoDeTeste == folhaBase, 8);
		await Segundos(1);
		Image? foto0 = Fotografar("user://feridafrost-0-repouso.png");
		bool luz = vestiu && foto0 != null && MedirALuz(foto0, folhaBase);
		Conferir(vestiu && luz, $"F0. (premissa) o corpo do robo e um Frost Demon NORMAL na forma base, inteiro, com o corpo \"{Corpos[0]}\" -- e a "
								+ $"luz do palco foi medida nele [forma {srv.FormaDoCorpoDeTeste(eu)}, luz {_luz.R:0.00}/{_luz.G:0.00}/{_luz.B:0.00}]");
		if (!vestiu || !luz) return;

		// ---------------------------------------------------------- F1. o controle: a evolucao inteira
		bool subiu = await Subir(true, evo1, FormasDeFrost.Caminho(Corpos[1]));
		Leitura? l1 = await Medir("user://feridafrost-1-inteira.png");
		Conferir(subiu && Inteira(l1),
				 $"F1. (controle) a 1a EVOLUCAO INTEIRA (o C, pelo fio): a regua acha os DOIS bracos do \"{Corpos[1]}\" na tela e nenhuma ferida [{l1}]");
		if (!subiu) return;

		// ---------------------------------------------------------- F2. a ferida, com a evolucao vestida
		bool chegou = await Apanhar();
		Leitura? l2 = await Medir("user://feridafrost-2-ferida.png");
		Conferir(chegou && FeridoSem(l2, esquerda: true),
				 "F2. O FROST FERIDO NA 1a EVOLUCAO: a ferida aparece no corpo da evolucao e o braco DIREITO decepado some -- de frente, o lado "
				 + $"ESQUERDO da imagem -- com o outro braco na tela [mascara chegou {chegou}; {l2}]");

		// ---------------------------------------------------------- F3. de costas
		srv.FerirParaFotoDeTeste(eu);   // (a tag de combate de novo: nenhuma costura no caminho; a mascara sai igual)
		bool deCostas = await Virar(Jandirus.Core.World.Facing.North, "_north");
		Leitura? l3 = await Medir("user://feridafrost-3-de-costas.png");
		Conferir(deCostas && FeridoSem(l3, esquerda: false),
				 $"F3. DE COSTAS o braco que falta TROCA DE LADO na imagem (o direito dela), e a ferida continua [virou {deCostas}; {l3}]");

		// ---------------------------------------------------------- F4. de frente, no repouso
		srv.FerirParaFotoDeTeste(eu);
		bool deFrente = await Virar(Jandirus.Core.World.Facing.South, "_south");
		bool desceu = deFrente && await Subir(false, base5, null);
		// O X DEVOLVE O BRACO, e e regra do jogo e nao desta cena: trocar de forma com o tronco ferido e a Revitalizing Metamorphosis do
		// Frost Demon (`GameServer.Metamorfosear`: o decepado volta com 10% de vida) -- a primeira rodada com o conserto fotografou o
		// repouso ferido e com os dois bracos, e o material das duas camadas dizia que nenhum faltava. O corpo apanha de novo no repouso,
		// e a mascara nova chega pela porta da F2 (o `Ferir`), agora na camada reciclada da evolucao vestindo a folha do repouso.
		bool voltou = desceu && await Ate(() => cli.Feridas.TryGetValue(eu, out MascaraDeFeridas m)
													 && !m.Perdeu(MascaraDeFeridas.Membro.BracoDir), 5);
		Nota($"o X devolveu o braco decepado (Revitalizing Metamorphosis): {voltou}");
		bool deNovo = desceu && await Apanhar();
		Leitura? l4 = await Medir("user://feridafrost-4-repouso.png");
		Conferir(deNovo && FeridoSem(l4, esquerda: true),
				 $"F4. O REPOUSO (o X), de frente, ferido de novo: tambem com a ferida e sem o braco direito [virou {deFrente}, desceu {desceu}, "
				 + $"apanhou de novo {deNovo}; {l4}]");

		// ---------------------------------------------------------- F5. o contra-exemplo: a regra de antes
		srv.FerirParaFotoDeTeste(eu);
		CharacterVisual.FeridaSoNoCorpoDeTeste = true;
		try
		{
			bool renasceu = await RenascerACamada(base5);
			Leitura? l5 = await Medir("user://feridafrost-5-defeito-limpo.png");
			Conferir(renasceu && !FeridoSem(l5, esquerda: true) && Inteira(l5),
					 "F5. CONTRA-EXEMPLO (defeito injetado: a ferida so no corpo da base, escondido): a camada da forma RENASCE com o corpo ja "
					 + $"ferido e nasce LIMPA e com o braco -- e a regua de F4 REPROVA [renasceu {renasceu}; {l5}]");
		}
		finally { CharacterVisual.FeridaSoNoCorpoDeTeste = false; }

		// ---------------------------------------------------------- F6. a camada renasce ferida
		srv.FerirParaFotoDeTeste(eu);
		bool renasceu2 = await RenascerACamada(base5);
		Leitura? l6 = await Medir("user://feridafrost-6-renascida.png");
		Conferir(renasceu2 && FeridoSem(l6, esquerda: true),
				 $"F6. A CAMADA DA FORMA QUE RENASCE com o corpo ja ferido ja nasce ferida e sem o braco direito [renasceu {renasceu2}; {l6}]");

		// ---------------------------------------------------------- F7. o pedaco que o corte solta some junto
		bool decepou = srv.DeceparParaFotoDeTeste(eu, "Braco esquerdo")
					   && await Ate(() => C?.Feridas.TryGetValue(eu, out MascaraDeFeridas m) == true
										  && m.Perdeu(MascaraDeFeridas.Membro.BracoEsq), 5);
		await Segundos(1);
		Leitura? l7 = await Medir("user://feridafrost-7-sem-os-dois-bracos.png");
		Conferir(decepou && SemPedacoSolto(l7),
				 "F7. SEM OS DOIS BRACOS (o esquerdo agora -- de frente ele cai do lado por onde sobe o rabo): nenhum pedaco do corpo fica solto "
				 + $"na tela, a ponta do rabo que o corte do braco separa some junto [decepou {decepou}; {l7}]");

		// ---------------------------------------------------------- F8. o contra-exemplo: o pedaco solto fica
		srv.FerirParaFotoDeTeste(eu);
		CharacterVisual.PedacoSoltoFicaDeTeste = true;
		try
		{
			bool renasceu3 = await RenascerACamada(base5);
			Leitura? l8 = await Medir("user://feridafrost-8-defeito-pedaco-solto.png");
			Conferir(renasceu3 && !SemPedacoSolto(l8) && l8 is { Soltos: >= SoltoNoDefeito },
					 "F8. CONTRA-EXEMPLO (defeito injetado: o pedaco que o corte de membro solta fica na tela): a camada renasce e a ponta do rabo "
					 + $"aparece FLUTUANDO ao lado do corpo -- e a regua de F7 REPROVA [renasceu {renasceu3}; {l8}]");
		}
		finally { CharacterVisual.PedacoSoltoFicaDeTeste = false; }

		// ---------------------------------------------------------- F9. a camada renasce sem pedaco solto
		srv.FerirParaFotoDeTeste(eu);
		bool renasceu4 = await RenascerACamada(base5);
		Leitura? l9 = await Medir("user://feridafrost-9-renascida-sem-solto.png");
		Conferir(renasceu4 && SemPedacoSolto(l9),
				 $"F9. A CAMADA QUE RENASCE sem os dois bracos ja nasce sem o pedaco solto [renasceu {renasceu4}; {l9}]");
	}

	// =====================================================================
	// A REGUA
	// =====================================================================
	/// <summary>O que a regua leu numa cena.</summary>
	/// <param name="BracoE">Pixels do quadro na faixa do braco, metade ESQUERDA da imagem; <paramref name="BracoENaTela"/>, quantos a foto mostra.</param>
	/// <param name="Miolo">Pixels de cor viva do miolo (cabeca e tronco); <paramref name="MioloNaTela"/>, quantos a foto mostra; e
	/// <paramref name="MioloFerido"/>, quantos destes sairam da cor do quadro.</param>
	/// <param name="ChaoComCorpo">Pixels do miolo que a foto do CHAO mostra com a cor do quadro: o boneco nao saiu dela.</param>
	/// <param name="Soltos">Pixels do quadro que a foto mostra SEPARADOS do pedaco maior que ela mostra -- ver <see cref="ContarSoltos"/>.</param>
	private readonly record struct Leitura(int BracoE, int BracoENaTela, int BracoD, int BracoDNaTela, int Miolo, int MioloNaTela,
										   int MioloFerido, int ChaoComCorpo, int Soltos, string Camada)
	{
		public double E => BracoE == 0 ? 0 : (double)BracoENaTela / BracoE;
		public double D => BracoD == 0 ? 0 : (double)BracoDNaTela / BracoD;
		public double NaTela => Miolo == 0 ? 0 : (double)MioloNaTela / Miolo;
		public double Ferida => MioloNaTela == 0 ? 0 : (double)MioloFerido / MioloNaTela;

		/// <summary>A cena diz alguma coisa: as faixas tem pixels, e a foto do chao saiu sem o boneco.</summary>
		public bool Valida => BracoE >= FaixaMinima && BracoD >= FaixaMinima && Miolo >= MioloMinimo && ChaoComCorpo * 5 < Miolo;

		public override string ToString() =>
			$"braco da esquerda {BracoENaTela}/{BracoE} ({E:P0}), da direita {BracoDNaTela}/{BracoD} ({D:P0}); miolo na tela {MioloNaTela}/{Miolo} "
			+ $"({NaTela:P0}), ferido {MioloFerido} ({Ferida:P0}); chao com corpo {ChaoComCorpo}; pedaco solto na tela {Soltos} px; {Camada}";
	}

	/// <summary>
	/// F7 e F9: os dois bracos fora da tela, o corpo na tela, e nenhum pedaco dele solto (ate <see cref="SoltoTolerado"/> pixels: um pixel
	/// do corpo com a cor da grama pode partir uma ponte de um pixel).
	/// </summary>
	private static bool SemPedacoSolto(Leitura? l) =>
		l is { Valida: true } d && d.NaTela >= MioloNaTela && d.E <= BracoFora && d.D <= BracoFora && d.Soltos <= SoltoTolerado;

	/// <summary>O controle (F1) e o defeito (F5): os dois bracos na tela, o corpo inteiro na tela, e a cor do quadro no miolo.</summary>
	private static bool Inteira(Leitura? l) =>
		l is { Valida: true } d && d.E >= BracoNaTela && d.D >= BracoNaTela && d.NaTela >= MioloNaTela && d.Ferida <= Limpo;

	/// <summary>Ferido e sem o braco DIREITO dele, que cai no lado da imagem que a direcao diz (<paramref name="esquerda"/>): o outro braco na tela.</summary>
	private static bool FeridoSem(Leitura? l, bool esquerda) =>
		l is { Valida: true } d && d.NaTela >= MioloNaTela && d.Ferida >= Ferido
		&& (esquerda ? d.E <= BracoFora && d.D >= BracoNaTela : d.D <= BracoFora && d.E >= BracoNaTela);

	/// <summary>
	/// A FAIXA QUE O SHADER CORTA COMO BRACO, com folga das divisas: o `Personagem.gdshader` corta o braco em `fuv.y` de 0,46 a 0,80 e
	/// `|fuv.x - 0,5| >= 0,165`, com `fuv` = o pixel sobre o quadro de lado a lado (a caixa da `BorraoDirecional.Caixa` vai do centro do
	/// primeiro pixel ao do ultimo). A regua conta so 0,50 a 0,76 e 0,20 pra fora -- o que o shader corta com certeza.
	/// </summary>
	private static int ZonaDoBraco(Image q, int x, int y)
	{
		float fx = x / (float)(q.GetWidth() - 1), fy = y / (float)(q.GetHeight() - 1);
		if (fy < 0.50f || fy > 0.76f || Math.Abs(fx - 0.5f) < 0.20f) return 0;
		return fx < 0.5f ? -1 : 1;
	}

	/// <summary>
	/// O MIOLO: a coluna do meio da cabeca ao fim do tronco, onde nao ha braco nem perna (`|fuv.x - 0,5| &lt; 0,13`). A regua ainda pede o
	/// pixel <see cref="Cercado"/>: o contorno da forma pinta a borda da silhueta, e la a cor muda sem ferida nenhuma.
	/// </summary>
	private static bool NoMiolo(Image q, int x, int y)
	{
		float fx = x / (float)(q.GetWidth() - 1), fy = y / (float)(q.GetHeight() - 1);
		return fy >= 0.30f && fy <= 0.76f && Math.Abs(fx - 0.5f) < 0.13f;
	}

	/// <summary>
	/// A CAMADA DE CORPO DA FORMA, pela arvore: a de ordem 1 que nao e o rabo (`CharacterVisual.NovaCamada`; o robo nasce Humano, sem rabo).
	/// Pela ordem e nao pelo caminho: a camada do repouso pode vestir a folha que veio do cache, sem caminho.
	/// </summary>
	private static AnimatedSprite2D? CamadaDaForma(CharacterVisual v) =>
		v.GetChildren().OfType<AnimatedSprite2D>()
		 .FirstOrDefault(s => s.SpriteFrames is { } sf && s.GetMeta("ordem", -1).AsInt32() == 1 && sf.ResourcePath != CharacterVisual.SpriteDoRabo);

	/// <summary>A camada do corpo da base (ordem 0), pela marca `src` que o `Trocar` escreve -- como no `RoboDoCorpoDoFrost`.</summary>
	private static AnimatedSprite2D? CamadaDoCorpo(CharacterVisual v, string folha) =>
		v.GetChildren().OfType<AnimatedSprite2D>()
		 .FirstOrDefault(s => s.SpriteFrames != null && s.GetMeta("ordem", -1).AsInt32() == 0 && s.GetMeta("src", "").AsString() == folha);

	/// <summary>
	/// A FOTO DO CHAO e a do palco, nessa ordem, e a leitura. O boneco some por tres quadros (`Visible`, so a raiz dele) e volta por mais
	/// tres antes da foto do palco; o quadro da camada e lido DEPOIS, e a leitura anota se ele mudou entre as duas fotos.
	/// </summary>
	private async System.Threading.Tasks.Task<Leitura?> Medir(string destino)
	{
		if (VisualDe(_eu) is not { } v || CamadaDaForma(v) is not { } forma)
		{
			Nota($"a regua nao achou a camada da forma ({destino})");
			return null;
		}
		string antes = $"{forma.Animation}#{forma.Frame}";
		v.Visible = false;
		await Quadros(3);
		Image? chao = Fotografar(destino.Replace(".png", "-chao.png"));
		v.Visible = true;
		await Quadros(3);
		Image? foto = Fotografar(destino);
		if (foto == null || chao == null || Quadro(forma) is not { } q) return null;
		string depois = $"{forma.Animation}#{forma.Frame}";

		int be = 0, beT = 0, bd = 0, bdT = 0, miolo = 0, mioloT = 0, ferido = 0, chaoComCorpo = 0;
		var visto = new bool[q.GetWidth() * q.GetHeight()];
		for (int y = 0; y < q.GetHeight(); y++)
			for (int x = 0; x < q.GetWidth(); x++)
			{
				Color cq = q.GetPixel(x, y);
				if (cq.A < 0.5f) continue;
				Vector2 tela = NaTela(forma, q, x, y);
				if (CorNaFoto(foto, tela) is not { } cf || CorNaFoto(chao, tela) is not { } cc) continue;
				bool naTela = !Perto(cf, cc, FolgaDoChao);
				visto[y * q.GetWidth() + x] = naTela;

				int braco = ZonaDoBraco(q, x, y);
				if (braco < 0) { be++; if (naTela) beT++; }
				else if (braco > 0) { bd++; if (naTela) bdT++; }
				else if (NoMiolo(q, x, y) && DeCorViva(cq) && Cercado(q, x, y))
				{
					miolo++;
					if (Parecido(cc, cq)) chaoComCorpo++;
					if (!naTela) continue;
					mioloT++;
					if (!Parecido(cf, cq)) ferido++;
				}
			}
		Nota($"pilha em {destino.GetFile()}: {Pilha(v)}");
		return new Leitura(be, beT, bd, bdT, miolo, mioloT, ferido, chaoComCorpo, ContarSoltos(visto, q.GetWidth(), q.GetHeight()),
						   $"camada {Nome(forma.SpriteFrames?.ResourcePath)} {depois}{(antes == depois ? "" : $" (era {antes} na foto do chao)")}");
	}

	/// <summary>
	/// O PEDACO SOLTO NA TELA, lido da FOTO e nao da faixa do shader: dos pixels do quadro que a foto mostra (<paramref name="visto"/>),
	/// os ligados entre si (8-vizinhanca) formam pedacos; conta os pixels de todo pedaco que nao e o maior. Nao sabe nada de corte de
	/// membro -- so ve que ha desenho flutuando separado do corpo. Pedaco de 1 pixel nao conta (um pixel da cor da grama, lido como
	/// chao, faz um pixel vizinho parecer solto).
	/// </summary>
	private static int ContarSoltos(bool[] visto, int w, int h)
	{
		var pedaco = new int[visto.Length];
		var tamanhos = new List<int> { 0 };
		var pilha = new Stack<int>();
		for (int i = 0; i < visto.Length; i++)
		{
			if (!visto[i] || pedaco[i] != 0) continue;
			int n = tamanhos.Count, tam = 0;
			tamanhos.Add(0);
			pedaco[i] = n;
			pilha.Push(i);
			while (pilha.Count > 0)
			{
				int p = pilha.Pop();
				tam++;
				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
					{
						int nx = p % w + dx, ny = p / w + dy;
						if (nx < 0 || ny < 0 || nx >= w || ny >= h || !visto[ny * w + nx] || pedaco[ny * w + nx] != 0) continue;
						pedaco[ny * w + nx] = n;
						pilha.Push(ny * w + nx);
					}
			}
			tamanhos[n] = tam;
		}
		int maior = tamanhos.Max();
		return maior > 1 ? tamanhos.Where(t => t > 1).Sum() - maior : 0;
	}

	/// <summary>
	/// A PILHA DO BONECO COMO O SHADER A VE, pra quem le o log: cada camada com ordem, se esta acesa, a folha e o quadro, e o que o material
	/// dela diz da ferida -- o modo, o braco cortado (o par ja no lado da IMAGEM) e se a caixa do quadro e a do quadro que ela desenha.
	/// </summary>
	private static string Pilha(CharacterVisual v) =>
		string.Join(" | ", v.GetChildren().OfType<AnimatedSprite2D>().Where(s => s.SpriteFrames != null).Select(s =>
		{
			string mat = "sem material";
			if (s.Material is ShaderMaterial m)
			{
				Vector2 amp = m.GetShaderParameter("amp_braco").AsVector2();
				Vector2 mn = m.GetShaderParameter("quadro_min").AsVector2();
				bool caixaCerta = s.SpriteFrames.HasAnimation(s.Animation)
								  && BorraoDirecional.Caixa(s.SpriteFrames.GetFrameTexture(s.Animation, s.Frame)).Min.DistanceTo(mn) < 1e-4f;
				mat = $"modo {m.GetShaderParameter("ferida_modo").AsInt32()} braco ({amp.X:0},{amp.Y:0}) caixa {(caixaCerta ? "certa" : "VELHA")}";
			}
			return $"o{s.GetMeta("ordem", -1).AsInt32()} {(s.Visible ? "acesa" : "apagada")} {Nome(s.SpriteFrames.ResourcePath)} "
				   + $"{s.Animation}#{s.Frame} {mat}";
		}));

	/// <summary>
	/// A LUZ DO PALCO, por canal -- a do `RoboDoCorpoDoFrost`: o meio-dia do jogo nao e branco neutro (o branco puro do quadro sai (220, 220,
	/// 210) na foto). A regua compara a cor da tela com a do quadro JA com esta luz.
	/// </summary>
	private Color _luz = new(1, 1, 1);

	/// <summary>A luz razoavel: escurece ou clareia um pouco, e nao apaga nem estoura um canal.</summary>
	private const float LuzMinima = 0.5f, LuzMaxima = 1.3f;

	/// <summary>
	/// Mede a <see cref="_luz"/> na foto do repouso INTEIRO: por canal, a mediana de tela / quadro no miolo do corpo da base. No repouso a
	/// camada da forma e a mesma folha no mesmo lugar, entao o que esta na tela ali e esse desenho -- escondido ou nao o node de baixo.
	/// </summary>
	private bool MedirALuz(Image foto, string folhaBase)
	{
		if (VisualDe(_eu) is not { } v || CamadaDoCorpo(v, folhaBase) is not { } baixo || Quadro(baixo) is not { } qb) return false;
		var r = new List<float>();
		var g = new List<float>();
		var b = new List<float>();
		for (int y = 1; y < qb.GetHeight() - 1; y++)
			for (int x = 1; x < qb.GetWidth() - 1; x++)
			{
				Color cq = qb.GetPixel(x, y);
				if (cq.A < 0.5f || !Cercado(qb, x, y) || CorNaFoto(foto, NaTela(baixo, qb, x, y)) is not { } ct) continue;
				if (cq.R8 >= 100) r.Add(ct.R / cq.R);
				if (cq.G8 >= 100) g.Add(ct.G / cq.G);
				if (cq.B8 >= 100) b.Add(ct.B / cq.B);
			}
		if (r.Count < 10 || g.Count < 10 || b.Count < 10) { Nota($"poucos pixels pra medir a luz ({r.Count}/{g.Count}/{b.Count})"); return false; }
		_luz = new Color(Mediana(r), Mediana(g), Mediana(b));
		return _luz.R is >= LuzMinima and <= LuzMaxima && _luz.G is >= LuzMinima and <= LuzMaxima && _luz.B is >= LuzMinima and <= LuzMaxima;
	}

	private static float Mediana(List<float> v)
	{
		v.Sort();
		return v[v.Count / 2];
	}

	/// <summary>O pixel e os oito vizinhos sao opacos -- longe do contorno que a forma pinta na borda.</summary>
	private static bool Cercado(Image q, int x, int y)
	{
		if (x < 1 || y < 1 || x > q.GetWidth() - 2 || y > q.GetHeight() - 2) return false;
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
				if (q.GetPixel(x + dx, y + dy).A < 0.5f) return false;
		return true;
	}

	/// <summary>O quadro que a camada esta desenhando agora, em RGBA8.</summary>
	private static Image? Quadro(AnimatedSprite2D s)
	{
		if (s.SpriteFrames is not { } f || !f.HasAnimation(s.Animation) || f.GetFrameCount(s.Animation) == 0) return null;
		int i = Math.Clamp(s.Frame, 0, f.GetFrameCount(s.Animation) - 1);
		if (f.GetFrameTexture(s.Animation, i)?.GetImage() is not { } img || img.IsEmpty()) return null;
		var copia = (Image)img.Duplicate();
		copia.Convert(Image.Format.Rgba8);
		return copia;
	}

	/// <summary>O centro do pixel (x, y) do quadro desta camada, na foto. Toda camada e `Centered` (`CharacterVisual.NovaCamada`).</summary>
	private static Vector2 NaTela(AnimatedSprite2D s, Image quadro, int x, int y)
	{
		float w = quadro.GetWidth(), h = quadro.GetHeight();
		float lx = s.FlipH ? w - (x + 0.5f) : x + 0.5f;
		return s.GetGlobalTransformWithCanvas() * (new Vector2(lx - w / 2f, y + 0.5f - h / 2f) + s.Offset);
	}

	private static Color? CorNaFoto(Image foto, Vector2 tela)
	{
		int x = Mathf.FloorToInt(tela.X), y = Mathf.FloorToInt(tela.Y);
		return x >= 0 && y >= 0 && x < foto.GetWidth() && y < foto.GetHeight() ? foto.GetPixel(x, y) : null;
	}

	private static bool DeCorViva(Color c) => Math.Max(c.R8, Math.Max(c.G8, c.B8)) >= Viva;

	/// <summary>Duas cores de TELA iguais dentro da folga, por canal.</summary>
	private static bool Perto(Color a, Color b, int folga) =>
		Math.Abs(a.R8 - b.R8) <= folga && Math.Abs(a.G8 - b.G8) <= folga && Math.Abs(a.B8 - b.B8) <= folga;

	/// <summary>A cor da tela e a do quadro sob a <see cref="_luz"/> do palco, por canal, dentro da <see cref="Folga"/>.</summary>
	private bool Parecido(Color tela, Color quadro) =>
		Math.Abs(tela.R8 - quadro.R8 * _luz.R) <= Folga && Math.Abs(tela.G8 - quadro.G8 * _luz.G) <= Folga
		&& Math.Abs(tela.B8 - quadro.B8 * _luz.B) <= Folga;

	private static string Nome(string? caminho) => caminho is { Length: > 0 } c ? c.GetFile().GetBaseName() : "(folha do cache)";

	// =====================================================================
	// O DE SEMPRE DOS ROBOS
	// =====================================================================
	private Image? Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto (headless nao renderiza)"); return null; }
		img.Convert(Image.Format.Rgba8);
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		Nota("foto salva em " + caminho);
		return img;
	}

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

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[dferidafrost] " + l);
		GD.Print(_falhas.Count == 0 ? "[dferidafrost] ===== TUDO OK =====" : $"[dferidafrost] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dferidafrost]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
