using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO BEBE (`--diagbebe`, 2026-10-18). O pedido do dono: *"faça com q Saiyajins, meio saiyajins, e humanos filhos de players tenham
/// sempre esse sprite por 1 hora no jogo, mostrando q eles acabaram de nascer, dps dessa 1 hora o jogador vira o sprite normal q ele fez na
/// criação com as roupas escolhidas etc, na forma de bebe eles so vao ser o sprite sem overlay"* -- e *"Baby boy se o personagem for homem e
/// baby girls se for mulher"*. Com janela e `--host`, nascendo Saiyajin no campo:
///   B1. o fotografo veste a ficha da criacao (o cabelo do Gohan pequeno e o gi dele) e vira recem-nascido: o corpo e a folha `Baby Boy` e
///       so ela esta acesa -- sem roupa, cabelo, olhos de gente e rabo; do lado, a menina de palco (uma Humana com cabelo e roupa na ficha)
///       e a `Baby Girl`, do mesmo jeito -- foto `bebe-1-recem-nascidos`;
///   B2. CONTRA-EXEMPLO (`CharacterVisual.BebeVesteTudoDeTeste`, o boneco que veste o que a ficha manda): a roupa, o cabelo e os olhos
///       voltam por cima da folha do bebe, e a regua de B1 reprova -- foto `bebe-2-defeito-veste-tudo`;
///   B3. o fim da hora: sobram dois segundos e o relogio de producao (`TickDosBebes`) o faz crescer -- o corpo volta a ser o da criacao, com
///       a roupa, o cabelo e os olhos, e o chat diz que ele cresceu; a menina cresce pelo mesmo `Crescer` -- foto `bebe-3-cresceram`.
/// O palco esta em `GameServer.BebePalco.cs`; o parto de verdade e da `--filhosteste` e da `--diagfilho`.
///
///     Godot --path . --host --rede 8563 --campoteste 8 --horateste 0.5 --semfauna --diagbebe --raca Saiyan --conta bancada_bebe --nome Gohan
/// </summary>
public partial class RoboDoBebe : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>A ficha que o filho escolheria na criacao -- a dele e a da menina.</summary>
	private const string CabeloDele = "Kid Gohan", RoupaDele = "res://Assets/Sprites/Clothes/Clothes_GohanGi.tres";
	private const string CabeloDela = "Female Ponytail", RoupaDela = "res://Assets/Sprites/Clothes/ClothesGiFemale.tres";

	private int _eu;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		CharacterVisual.BebeVesteTudoDeTeste = false;
		S?.PalcoB_Limpar(_eu);
		Fechar();
	}

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private bool Disse(string trecho) => _ditas.Exists(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private string UltimasDitas() => string.Join(" | ", _ditas.TakeLast(3));

	private static CharacterVisual? VisualDe(int id) => World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>A folha do corpo e as camadas acesas de um boneco, pra regua e pro log.</summary>
	private static (string Folha, string[] Acesas) Desenho(int id) =>
		VisualDe(id) is { } v ? (v.FolhaDoCorpoDeTeste, v.CamadasAcesasDeTeste()) : ("", []);

	private static string Contar((string Folha, string[] Acesas) d) => $"{d.Folha.Split('/')[^1]}: {string.Join(", ", d.Acesas)}";

	/// <summary>A regua de B1: o corpo e a folha do bebe pedida, e nada alem dele esta aceso.</summary>
	private static bool SoOBebe(int id, string folha) => Desenho(id) is var d && d.Folha == folha && d.Acesas.SequenceEqual(["corpo"]);

	/// <summary>A regua de B3: o corpo saiu da folha do bebe, e a roupa, o cabelo e os olhos da ficha estao acesos.</summary>
	private static bool ComoNaCriacao(int id) =>
		Desenho(id) is var d && d.Folha.Length > 0 && d.Folha != VisualCatalog.CorpoDeBebeM && d.Folha != VisualCatalog.CorpoDeBebeF
		&& d.Acesas.Contains("corpo") && d.Acesas.Contains("roupa0") && d.Acesas.Contains("cabelo") && d.Acesas.Contains("olho");

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = _eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dbebe] ===== O FILHO QUE NASCE BEBE (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);
		cli.Falou += Ouvir;
		try
		{
			// ---------------------------------------------------------- montagem: a ficha da criacao, ainda grande
			bool vestiu = srv.PalcoB_VestirAFicha(eu, CabeloDele, RoupaDele);
			bool grande = vestiu && await Ate(() => ComoNaCriacao(eu), 8);
			Conferir(grande, $"(montagem) o fotografo veste a ficha da criacao -- o gi e o cabelo do Gohan pequeno -- e o boneco a mostra [{Contar(Desenho(eu))}]");

			// ---------------------------------------------------------- B1. os dois recem-nascidos
			bool nasceu = srv.PalcoB_Nascer(eu);
			int menina = srv.PalcoB_Menina(eu, 1.5f, CabeloDela, RoupaDela);
			bool ele = nasceu && await Ate(() => SoOBebe(eu, VisualCatalog.CorpoDeBebeM), 8);
			bool ela = menina > 0 && await Ate(() => SoOBebe(menina, VisualCatalog.CorpoDeBebeF), 8);
			await Segundos(0.6);
			Fotografar("user://bebe-1-recem-nascidos.png");
			Conferir(ele && ela,
					 "B1. O RECEM-NASCIDO E SO O SPRITE DO BEBE: o fotografo (homem) na `Baby Boy` e a menina na `Baby Girl`, sem roupa, cabelo, olhos "
					 + $"de gente nem rabo por cima -- e a menina chega pelo pacote de outro corpo [ele {Contar(Desenho(eu))}; ela {Contar(Desenho(menina))}; "
					 + $"falta {srv.PalcoB_Restante(eu):0} s]");

			// ---------------------------------------------------------- B2. o contra-exemplo: o boneco que veste tudo
			CharacterVisual.BebeVesteTudoDeTeste = true;
			try
			{
				srv.PalcoB_Reapresentar(eu);
				bool vestido = await Ate(() => Desenho(eu).Acesas.Contains("roupa0"), 6);
				await Segundos(0.4);
				Fotografar("user://bebe-2-defeito-veste-tudo.png");
				Conferir(vestido && !SoOBebe(eu, VisualCatalog.CorpoDeBebeM),
						 "B2. CONTRA-EXEMPLO (defeito injetado: o boneco veste o que a ficha manda): a roupa, o cabelo e os olhos voltam por cima da "
						 + $"folha do bebe -- a regua de B1 REPROVA [{Contar(Desenho(eu))}]");
			}
			finally { CharacterVisual.BebeVesteTudoDeTeste = false; }
			srv.PalcoB_Reapresentar(eu);
			await Ate(() => SoOBebe(eu, VisualCatalog.CorpoDeBebeM), 6);

			// ---------------------------------------------------------- B3. a hora acaba
			srv.PalcoB_FaltaPouco(eu, 2);
			bool cresceu = await Ate(() => srv.PalcoB_Restante(eu) == 0 && ComoNaCriacao(eu) && Disse("Você cresceu"), 12);
			srv.PalcoB_MeninaCresce();
			bool elaCresceu = await Ate(() => ComoNaCriacao(menina), 8);
			await Segundos(0.6);
			Fotografar("user://bebe-3-cresceram.png");
			Conferir(cresceu && elaCresceu,
					 "B3. A HORA ACABA E ELES CRESCEM: o relogio de producao zera a hora do fotografo e o corpo volta a ser o da criacao, com o gi, o "
					 + $"cabelo e os olhos -- e o chat diz que ele cresceu; a menina cresce pelo mesmo `Crescer` [ele {Contar(Desenho(eu))}; "
					 + $"ela {Contar(Desenho(menina))}; ouvi: {UltimasDitas()}]");
		}
		finally { cli.Falou -= Ouvir; }
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
		foreach (string l in _linhas) GD.Print("[dbebe] " + l);
		GD.Print(_falhas.Count == 0 ? "[dbebe] ===== TUDO OK =====" : $"[dbebe] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dbebe]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
