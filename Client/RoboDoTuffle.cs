using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Verbos;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO NEO TUFFLE (`--diagtuffle`, 2026-10-18). O pedido do dono: a maquina dos Tsufurujins cria um Neo Tuffle que POSSUI jogadores
/// nocauteados, de cabelo branco-acinzentado -- o proprio Tsufurujin vira o Neo Tuffle, ele entra no corpo do nocauteado (o dono so
/// assiste) e sai quando o corpo cai. Com janela e `--host`, em DUAS corridas (a raca do robo escolhe qual; o palco esta em
/// `GameServer.TufflePalco.cs`, e as regras sao da `--tuffleteste`):
///
/// O ROBO TSUFURUJIN (o Baby) usa a MAQUINA pelo menu E, como o dedo do jogador -- o palco so poe o mainframe aos pes dele e da a tecnologia
/// e o zeni:
///   N1. "Instalar laboratorio...": pra ele so o Neo Tuffle Lab (os dois do humano nao aparecem) -- foto `tuffle-1-laboratorio`; o clique
///       instala;
///   N2. "Virar Neo Tuffle...": a conversao nas duas moedas -- foto `tuffle-2-conversao`; o clique em zeni converte, o chat conta, e o
///       "Possuir um nocauteado" acende no cliente -- foto `tuffle-2b-virou`.
///   N2b. (2026-10-20, o dono: *"faca com q os neotuffle tenham esse sprite"* -- `Bebi 1 Alt` -- e *"ele fica careca"*) O CORPO NOVO, NA
///       HORA: vestido antes da maquina com o cabelo e a roupa da criacao, ele sai dela na folha do Baby -- careca e sem os olhos de gente,
///       com a roupa da criacao por cima (a escolha do dono; a foto `tuffle-2b-virou` e esta). Dois CONTRA-EXEMPLOS: o pacote sem a marca do
///       corpo (`TuffleSemCorpoDoBabyDeTeste`, ele volta a cara da criacao) -- foto `tuffle-2c-defeito-sem-corpo-do-baby`; e o boneco que veste
///       o cabelo e os olhos da ficha por cima do Baby (`CharacterVisual.TuffleComCabeloDeTeste`) -- foto `tuffle-2d-defeito-com-cabelo`.
///
/// O ROBO SAIYAJIN (o Vegeta) e o HOSPEDE, possuido por um Neo Tuffle de palco (o Baby -- careca de fraque na ficha, que o mundo ve na folha do
/// Baby, de fraque por cima):
///   N3. o Baby do lado do Vegeta de pe, na folha dele -- foto `tuffle-3-o-neo-tuffle`;
///   N4. o Vegeta cai e o Baby entra nele: o cliente do Vegeta esconde o proprio boneco e assiste o corpo do Baby -- que o mundo ve com o
///       cabelo e a armadura do Vegeta, o cabelo BRANCO-ACINZENTADO (o do Saiyajin, que nao aceita cor nenhuma) -- foto `tuffle-4-possuido`;
///       CONTRA-EXEMPLO (`TuffleSemCabeloBrancoDeTeste`): o cabelo de sempre, e a regua do cabelo reprova -- foto
///       `tuffle-4b-defeito-sem-cabelo-branco`;
///   N5. o corpo possuido anda e a camera do Vegeta vai junto -- foto `tuffle-5-assistindo`;
///   N6. o corpo possuido cai: o Baby e expulso (de volta a folha dele, de pe ao lado) e o Vegeta volta caido onde o corpo caiu, e o
///       chat diz que o Tuffle saiu -- foto `tuffle-6-expulso`.
///
///     Godot --path . --host --rede 8566 --campoteste 8 --horateste 0.5 --semfauna --diagtuffle --raca Tsujin --conta bancada_dtufflelab --nome Baby
///     Godot --path . --host --rede 8564 --campoteste 8 --horateste 0.5 --semfauna --diagtuffle --raca Saiyan --conta bancada_dtuffle --nome Vegeta
/// </summary>
public partial class RoboDoTuffle : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;
	private static MenuDeInteracao? E => MenuDeInteracao.Instancia;

	/// <summary>O cabelo e a roupa do Vegeta -- os que o corpo possuido vai vestir, com o cabelo pintado.</summary>
	private const string CabeloDele = "Vegeta", RoupaDele = "res://Assets/Sprites/Clothes/Clothes_SaiyanSuit.tres";

	private int _eu;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	// O DEFEITO DO CLIENTE NUNCA SOBREVIVE AO ROBO, nem a uma saida no meio.
	public override void _ExitTree() => CharacterVisual.TuffleComCabeloDeTeste = false;

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		CharacterVisual.TuffleComCabeloDeTeste = false;
		if (E is { NaTela: true }) ApertarE();
		S?.PalcoN_Limpar(_eu);
		Fechar();
	}

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private bool Disse(string trecho) => _ditas.Exists(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private string UltimasDitas() => string.Join(" | ", _ditas.TakeLast(3));

	private static Node2D? CorpoDe(int id) => World.Instancia?.CorpoDeTeste(id);

	private static CharacterVisual? VisualDe(int id) => CorpoDe(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>O cabelo deste corpo esta pintado de branco-acinzentado (a tinta do Tuffle, no modo de soma)?</summary>
	private static bool CabeloBranco(int id)
	{
		if (VisualDe(id)?.TintaDoCabeloDeTeste is not { } t) return false;
		Rgb c = Appearance.CinzaDoTuffle;
		return Mathf.Abs(t.Tinta.X - c.R / 255f) < 0.02f && Mathf.Abs(t.Tinta.Y - c.G / 255f) < 0.02f && Mathf.Abs(t.Tinta.Z - c.B / 255f) < 0.02f;
	}

	private static string Cabelo(int id) => VisualDe(id) is { } v ? $"{v.CabeloDeTeste.Split('/')[^1]} {v.TintaDoCabeloDeTeste}" : "(sem boneco)";

	/// <summary>A folha do corpo e as camadas acesas de um boneco, pra regua e pro log (as do `RoboDoBebe`).</summary>
	private static (string Folha, string[] Acesas) Desenho(int id) =>
		VisualDe(id) is { } v ? (v.FolhaDoCorpoDeTeste, v.CamadasAcesasDeTeste()) : ("", []);

	private static string Contar((string Folha, string[] Acesas) d) => $"{d.Folha.Split('/')[^1]}: {string.Join(", ", d.Acesas)}";

	/// <summary>
	/// (2026-10-20) O Neo Tuffle no proprio corpo: a folha do Baby, careca e sem os olhos de gente -- e a roupa da ficha por cima (a escolha do
	/// dono), que a regua exige acesa pra nao passar com um boneco que apagasse tudo.
	/// </summary>
	private static bool NoCorpoDoBaby(int id) =>
		Desenho(id) is var d && d.Folha == VisualCatalog.CorpoDoNeoTuffle && d.Acesas.Contains("corpo") && d.Acesas.Contains("roupa0")
		&& !d.Acesas.Contains("cabelo") && !d.Acesas.Contains("olho");

	/// <summary>O corpo da criacao: fora da folha do Baby, com a roupa e o cabelo da ficha acesos.</summary>
	private static bool ComoNaCriacao(int id) =>
		Desenho(id) is var d && d.Folha.Length > 0 && d.Folha != VisualCatalog.CorpoDoNeoTuffle
		&& d.Acesas.Contains("corpo") && d.Acesas.Contains("roupa0") && d.Acesas.Contains("cabelo");

	private static float DistanciaDosCorpos(int a, int b) =>
		CorpoDe(a) is { } x && CorpoDe(b) is { } y ? x.Position.DistanceTo(y.Position) : float.NaN;

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = _eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dtuffle] ===== O NEO TUFFLE (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);
		cli.Falou += Ouvir;
		try
		{
			if (srv.PalcoN_Raca(eu) == "Tsujin") await AMaquina(srv, eu);
			else await APosse(cli, srv, eu);
		}
		finally { cli.Falou -= Ouvir; }
	}

	// =====================================================================
	// O ROBO TSUFURUJIN: A MAQUINA, PELO MENU E
	// =====================================================================
	private async Tarefa AMaquina(Jandirus.Server.GameServer srv, int eu)
	{
		bool temMainframe = srv.PalcoN_Mainframe(eu) > 0;
		srv.PalcoN_TechEZeni(eu);
		// (2026-10-20) A FICHA DA CRIACAO, VESTIDA: um cabelo e uma roupa (os do Vegeta da outra corrida -- qualquer um serve), pra que o "sai
		// careca e sem roupa" do N2b meca alguma coisa. Com o personagem de linha de comando pelado, a regua passaria sem o conserto.
		bool vestiu = srv.PalcoB_VestirAFicha(eu, CabeloDele, RoupaDele);
		bool comoNaCriacao = vestiu && await Ate(() => ComoNaCriacao(eu), 8);
		Conferir(comoNaCriacao, $"(montagem) o Tsufurujin veste um cabelo e uma roupa na ficha, e o boneco os mostra [{Contar(Desenho(eu))}]");
		await Segundos(0.8);

		// ---------------------------------------------------------- N1. o laboratorio
		ApertarE();
		bool abriu = await Ate(() => E is { NaTela: true }, 4);
		bool foiAosLabs = abriu && Apertar("Instalar laboratório...");
		bool soODele = await Ate(() => E?.BotoesDesenhados() is { } b && b.Contains("Neo Tuffle Lab")
										 && !b.Contains("Android Lab") && !b.Contains("Bio-Android Lab"), 3);
		string labs = string.Join(" | ", E?.BotoesDesenhados() ?? []);
		await Quadros(4);
		Fotografar("user://tuffle-1-laboratorio.png");
		bool instalou = soODele && Apertar("Neo Tuffle Lab")
						&& await Ate(() => srv.PalcoN_AMaquina(eu).Lab == 3 && Disse("vira um Neo Tuffle Lab"), 6);
		Conferir(temMainframe && foiAosLabs && soODele && instalou,
				 "N1. O TSUFURUJIN NO MAINFRAME: \"Instalar laboratorio...\" mostra so o Neo Tuffle Lab (os dois do humano nao aparecem pra ele), e "
				 + $"o clique instala [botoes: {labs}; lab {srv.PalcoN_AMaquina(eu).Lab}; ouvi: {UltimasDitas()}]");
		await Fechado();

		// ---------------------------------------------------------- N2. a conversao
		ApertarE();
		await Ate(() => E is { NaTela: true }, 4);
		bool foiAConversao = Apertar("Virar Neo Tuffle...");
		bool duasMoedas = await Ate(() => E?.BotoesDesenhados() is { } b && b.Contains("Neo Tuffle (1.000.000 zeni)")
											 && b.Contains("Neo Tuffle (2.000.000 recursos)"), 3);
		string conversao = string.Join(" | ", E?.BotoesDesenhados() ?? []);
		await Quadros(4);
		Fotografar("user://tuffle-2-conversao.png");
		bool converteu = duasMoedas && Apertar("Neo Tuffle (1.000.000 zeni)")
						 && await Ate(() => srv.PalcoN_AMaquina(eu).NeoTuffle && Disse("a máquina se fecha sobre você"), 6);
		await Fechado();
		// O BOTAO, e nao so a linha da tabela: a aba Other desenha os verbs REGISTRADOS no cliente (`Habilidades`), e a tabela so os esconde
		// -- a primeira versao desta regua olhava so a tabela e passou com o botao faltando (a contraprova por mutacao de 2026-10-19 achou).
		bool verbAceso = await Ate(() => Verbos.Todos.Any(v => v.Chave == "hab:tuffle_possuir" && v.Categoria == Verbos.Outros)
										 && RequisitosDosVerbos.Linha("hab:tuffle_possuir") is { } l && l.Requisito.Atende(PosseDoCliente.Atual), 6);
		// (2026-10-20) O CORPO NOVO CHEGA SEM TROCAR DE ZONA: a conversao reapresenta a aparencia (`VirarNeoTuffle`).
		bool viraOBaby = converteu && await Ate(() => NoCorpoDoBaby(eu), 6);
		await Segundos(0.4);
		Fotografar("user://tuffle-2b-virou.png");
		Conferir(foiAConversao && duasMoedas && converteu && verbAceso,
				 "N2. \"VIRAR NEO TUFFLE...\": a conversao nas duas moedas; o clique em zeni converte, o chat conta, e o \"Possuir um nocauteado\" "
				 + $"acende no cliente [botoes: {conversao}; Neo Tuffle {srv.PalcoN_AMaquina(eu).NeoTuffle}; verb {verbAceso}; ouvi: {UltimasDitas()}]");
		Conferir(comoNaCriacao && viraOBaby,
				 "N2b. O NEO TUFFLE ANDA NA FOLHA DO BABY: vestido na ficha, ele sai da maquina no `Bebi 1 Alt` -- careca e sem os olhos de gente, "
				 + $"com a roupa da ficha por cima --, na hora [{Contar(Desenho(eu))}]");

		// ---------------------------------------------------------- N2c. contra-exemplo: o pacote sem a marca do corpo
		srv.PalcoN_SemCorpoDoBaby(eu, true);
		bool voltouACara = await Ate(() => ComoNaCriacao(eu), 6);
		await Segundos(0.4);
		Fotografar("user://tuffle-2c-defeito-sem-corpo-do-baby.png");
		string naCara = Contar(Desenho(eu));
		srv.PalcoN_SemCorpoDoBaby(eu, false);
		bool babyDeNovo = await Ate(() => NoCorpoDoBaby(eu), 6);
		Conferir(voltouACara && babyDeNovo,
				 "N2c. CONTRA-EXEMPLO (defeito injetado: o pacote sem a marca do corpo do Neo Tuffle): ele volta a cara da criacao, de cabelo e roupa -- "
				 + $"a regua do N2b REPROVA [no defeito {naCara}; de volta {Contar(Desenho(eu))}]");

		// ---------------------------------------------------------- N2d. contra-exemplo: o boneco que veste o cabelo e os olhos da ficha
		bool comCabelo;
		string noDefeito;
		CharacterVisual.TuffleComCabeloDeTeste = true;
		try
		{
			srv.PalcoB_Reapresentar(eu);
			comCabelo = await Ate(() => Desenho(eu) is var d && d.Folha == VisualCatalog.CorpoDoNeoTuffle && d.Acesas.Contains("cabelo")
										 && d.Acesas.Contains("olho"), 6);
			await Segundos(0.4);
			Fotografar("user://tuffle-2d-defeito-com-cabelo.png");
			noDefeito = Contar(Desenho(eu));
		}
		finally { CharacterVisual.TuffleComCabeloDeTeste = false; }
		srv.PalcoB_Reapresentar(eu);
		bool carecaDeNovo = await Ate(() => NoCorpoDoBaby(eu), 6);
		Conferir(comCabelo && carecaDeNovo,
				 "N2d. CONTRA-EXEMPLO (defeito injetado: o boneco veste o cabelo e os olhos da ficha): eles voltam por cima da folha do Baby -- a "
				 + $"regua do N2b REPROVA [no defeito {noDefeito}; de volta {Contar(Desenho(eu))}]");
	}

	/// <summary>Fecha o menu E se ele estiver aberto, e espera ele sair da tela.</summary>
	private async Tarefa Fechado()
	{
		if (E is { NaTela: true }) ApertarE();
		await Ate(() => E is not { NaTela: true }, 3);
		await Quadros(2);
	}

	// =====================================================================
	// O ROBO SAIYAJIN: A POSSE, VISTA PELO DONO DO CORPO
	// =====================================================================
	private async Tarefa APosse(GameClient cli, Jandirus.Server.GameServer srv, int eu)
	{
		// ---------------------------------------------------------- N3. o Baby do lado do Vegeta
		bool vestiu = srv.PalcoB_VestirAFicha(eu, CabeloDele, RoupaDele);
		int baby = srv.PalcoN_Tuffle(eu, 1.5f);
		bool viu = baby > 0 && await Ate(() => NoCorpoDoBaby(baby) && VisualDe(eu) is { } meu && meu.CamadasAcesasDeTeste().Contains("roupa0"), 8);
		await Segundos(0.6);
		Fotografar("user://tuffle-3-o-neo-tuffle.png");
		Conferir(vestiu && viu,
				 "N3. o Vegeta de pe, de cabelo do Vegeta e armadura, e do lado o Baby -- o Neo Tuffle de palco, na folha do Baby, de fraque por cima e "
				 + $"sem os olhos de gente [Baby {baby}: {Contar(Desenho(baby))}]");
		// (2026-10-28, o pedido 144) o cabelo do Vegeta ANTES da posse, na tela dele -- e a regua do N6b
		var cabeloDoVegetaAntes = VisualDe(eu)?.TintaDoCabeloDeTeste;
		string arquivoDoCabeloAntes = VisualDe(eu)?.CabeloDeTeste ?? "";

		// ---------------------------------------------------------- N4. a posse
		// (Possuido, o corpo e o do Vegeta: a folha da raca dele, o cabelo e a armadura -- e nao a folha do Baby, que e a do Neo Tuffle no proprio
		// corpo.)
		bool entrou = baby > 0 && srv.PalcoN_Possuir(eu, baby);
		bool assiste = entrou && await Ate(() => cli.PossuidoPor == baby && VisualDe(eu) is { Visible: false }
												 && VisualDe(baby) is { TemCabeloDeTeste: true } v && v.CabeloDeTeste.Contains("Vegeta")
												 && v.FolhaDoCorpoDeTeste != VisualCatalog.CorpoDoNeoTuffle
												 && v.CamadasAcesasDeTeste().Contains("roupa0") && CabeloBranco(baby), 8);
		await Segundos(0.6);
		Fotografar("user://tuffle-4-possuido.png");
		Conferir(assiste && Disse("um Neo Tuffle entrou no seu corpo"),
				 "N4. O BABY ENTRA NO VEGETA CAIDO: o cliente do Vegeta esconde o proprio boneco e assiste o corpo do Baby -- que o mundo ve com o "
				 + $"cabelo e a armadura do Vegeta e o CABELO BRANCO-ACINZENTADO --, e o chat avisa [possuido por {cli.PossuidoPor}; "
				 + $"cabelo {Cabelo(baby)}; ouvi: {UltimasDitas()}]");

		srv.PalcoN_SemCabeloBranco(baby, true);
		bool semBranco = await Ate(() => !CabeloBranco(baby), 6);
		await Segundos(0.4);
		Fotografar("user://tuffle-4b-defeito-sem-cabelo-branco.png");
		srv.PalcoN_SemCabeloBranco(baby, false);
		bool voltouOBranco = await Ate(() => CabeloBranco(baby), 6);
		Conferir(semBranco && voltouOBranco,
				 "N4b. CONTRA-EXEMPLO (defeito injetado: o pacote sem o cabelo do Tuffle): o corpo possuido aparece com o cabelo de sempre -- a regua do "
				 + $"cabelo REPROVA [sem o branco no defeito {semBranco}; de volta {voltouOBranco}]");

		// ---------------------------------------------------------- N5. o dono assiste de onde o corpo esta
		Vector2 ondeEstava = CorpoDe(baby)?.Position ?? Vector2.Zero;
		srv.PalcoN_Andar(baby, -3);
		bool foiJunto = await Ate(() => CorpoDe(baby) is { } dele && dele.Position.DistanceTo(ondeEstava) > 60 && DistanciaDosCorpos(eu, baby) < 6, 6);
		await Segundos(0.5);
		Fotografar("user://tuffle-5-assistindo.png");
		Conferir(foiJunto, $"N5. O CORPO POSSUIDO ANDA E A CAMERA DO VEGETA VAI JUNTO [a {DistanciaDosCorpos(eu, baby):0.#} px dele]");

		// ---------------------------------------------------------- N6. a expulsao
		srv.PalcoN_DerrubarOPossuido(baby);
		bool expulso = await Ate(() => cli.PossuidoPor == 0 && srv.PalcoN_Robo(eu) is { PossuidoPor: 0, Caido: true } && VisualDe(eu) is { Visible: true }
									   && VisualDe(baby) is { TemCabeloDeTeste: false } && !CabeloBranco(baby) && NoCorpoDoBaby(baby)
									   && Disse("o Neo Tuffle saiu do seu corpo"), 10);
		await Segundos(0.8);
		Fotografar("user://tuffle-6-expulso.png");
		Conferir(expulso,
				 "N6. O CORPO POSSUIDO CAI E O BABY E EXPULSO: ele volta ao proprio corpo (a folha do Baby, careca), de pe ao lado, e o Vegeta volta a "
				 + $"aparecer caido onde o corpo caiu -- e o chat diz que o Tuffle saiu [possuido por {cli.PossuidoPor}; robo {srv.PalcoN_Robo(eu)}; "
				 + $"ouvi: {UltimasDitas()}]");

		// (2026-10-28, o pedido 144) *"ao sair do corpo n faz o cabelo do personagem invadido voltar a cor original"*: o boneco do Vegeta, na tela
		// dele, volta com o MESMO cabelo de antes da posse -- o arquivo e a tinta (a do Saiyajin e nenhuma) -- e nao o branco do Tuffle.
		bool cabeloDeSempre = await Ate(() => VisualDe(eu) is { TemCabeloDeTeste: true } meu && meu.CabeloDeTeste == arquivoDoCabeloAntes
															  && !CabeloBranco(eu) && Equals(meu.TintaDoCabeloDeTeste, cabeloDoVegetaAntes), 6);
		Conferir(cabeloDeSempre,
				 "N6b. O VEGETA VOLTA COM O CABELO DE SEMPRE (o pedido 144): o arquivo e a tinta de antes da posse, e nao o branco do Tuffle "
				 + $"[antes {arquivoDoCabeloAntes.Split('/')[^1]} {cabeloDoVegetaAntes}; agora {Cabelo(eu)}]");
	}

	/// <summary>A TECLA E DE VERDADE: um evento de teclado empurrado no viewport (o caminho do `_UnhandledInput`) -- como a `--diagdrill`.</summary>
	private void ApertarE()
	{
		var ev = new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true };
		GetViewport().PushInput(ev);
	}

	/// <summary>Aperta um botao do menu aberto pelo rotulo -- o mesmo sinal que o dedo emite. Falso se ele nao estava la.</summary>
	private bool Apertar(string rotulo)
	{
		if (E?.ApertarDesenhado(rotulo) == true) return true;
		_linhas.Add(E is { NaTela: true } ? $"  --     (\"{rotulo}\" nao esta no menu, que tem: {string.Join(" | ", E.BotoesDesenhados())})"
										  : $"  --     (\"{rotulo}\": o menu nem estava aberto)");
		return false;
	}

	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
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
		foreach (string l in _linhas) GD.Print("[dtuffle] " + l);
		GD.Print(_falhas.Count == 0 ? "[dtuffle] ===== TUDO OK =====" : $"[dtuffle] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dtuffle]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
