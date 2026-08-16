using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// UMA COISA DE ESFERA NO CHAO -- a Estatua do Dragao, uma das sete, ou o dragao invocado.
///
/// ============================ IRMA DA <see cref="ObraDesenhada"/>, E DE PROPOSITO ============================
/// Mesma ancora (a base da celula), mesmo Y-sort, mesmo saneamento de nome de animacao, mesma
/// reclamacao alta quando falta arte. Copiar a forma e o certo aqui: um sprite ancorado num ponto da
/// zona ja tem um jeito de existir neste cliente, e inventar um segundo faria a esfera se comportar
/// diferente da bancada ao lado dela sem ninguem saber por que.
///
/// **O QUE MUDA E DE ONDE VEM O CAMINHO.** A obra recebe o `res://` pronto do servidor porque ele sai
/// do `construcoes.json`, que e dado extraido. A esfera recebe um SIMBOLO ("comum", "namek",
/// "estatua", "shenron", "porunga") e o traduz aqui, no <see cref="FolhaDe"/> -- e a regra 0.1 da
/// casa: *"simbolo no Core, caminho `res://` no cliente"*. O servidor nao conhece a arvore de assets.
/// =========================================================================================================
/// </summary>
public partial class EsferaDesenhada : Node2D
{
	public Protocol.CoisaDeEsfera Tipo;

	/// <summary>1..7 na esfera, 0 no resto. Decide o estado do sprite e o leque de pixels.</summary>
	public int Numero;

	/// <summary>O simbolo da folha. Ver <see cref="FolhaDe"/>.</summary>
	public string Folha = "comum";

	/// <summary>O set esta apagado? A esfera vira "inactive" e para de pulsar.</summary>
	public bool Inerte;

	private AnimatedSprite2D? _sprite;

	/// <summary>
	/// ============================ O SIMBOLO VIRA CAMINHO **AQUI**, E SO AQUI ============================
	/// As quatro folhas ja existem no repo e foram conferidas na Fase 0:
	///   * `Misc/Dragonballs.tres` -- oito animacoes, "1".."7" e "inactive", 32x32 (o set da Terra);
	///   * `Misc/dragonball.tres` -- as mesmas oito (o set de Namek, `dragonball.dmi` no DM);
	///   * `Character Icons/Dragon.tres` -- "porunga" e "shenron", 256x353;
	///   * `Misc/DragonStatue.tres` -- a estatua, 32x32.
	///
	/// A CAIXA DOS NOMES E A ARMADILHA QUE A FASE 0 APONTOU: o DM usa `icon_state = "Shenron"` com
	/// maiuscula e o `.tres` convertido guarda "shenron". Quem resolve isso e o <see cref="Sanear"/>,
	/// que e a MESMA funcao que a `ObraDesenhada` usa -- e por isso ela nao pode divergir daqui.
	/// ================================================================================================
	/// </summary>
	public static (string Arte, string Estado) FolhaDe(Protocol.CoisaDeEsfera tipo, string folha, int numero,
													   bool inerte) => tipo switch
	{
		Protocol.CoisaDeEsfera.Estatua =>
			("res://Assets/Sprites/Misc/DragonStatue.tres", ""),

		Protocol.CoisaDeEsfera.Dragao =>
			("res://Assets/Sprites/Character Icons/Dragon.tres",
			 folha == "porunga" ? "porunga" : "shenron"),

		_ => (folha == "namek"
				? "res://Assets/Sprites/Misc/dragonball.tres"
				: "res://Assets/Sprites/Misc/Dragonballs.tres",
			  Esferas.EstadoDoSprite(numero, inerte)),
	};

	public override void _Ready()
	{
		YSortEnabled = true;

		// O DRAGAO DESENHA POR CIMA DE QUEM PASSA. Ele tem 353 px de altura ancorados numa celula de
		// 32 -- o corpo que anda pelo pe dele esta visualmente DENTRO da figura, e o Y-sort desempata
		// isso pela sorte. E o mesmo caso (e a mesma solucao) da arvore na `ObraDesenhada`: o
		// original resolve com PLANO e nao com ordem.
		ZIndex = Tipo == Protocol.CoisaDeEsfera.Dragao ? 1 : 0;

		MontarSprite();
	}

	/// <summary>Tipos que ja reclamaram nesta sessao -- ver a mesma regra na `ObraDesenhada`.</summary>
	private static readonly HashSet<string> _jaReclamou = [];

	private void Reclamar(string arte, string estado, string porque)
	{
		if (!_jaReclamou.Add($"{arte}|{estado}")) return;
		GD.PushError($"[esfera] sem desenho ({porque}, arte='{arte}', estado='{estado}') -- "
					 + "vai aparecer como bolinha dourada.");
	}

	private void MontarSprite()
	{
		(string arte, string estado) = FolhaDe(Tipo, Folha, Numero, Inerte);

		if (!ResourceLoader.Exists(arte)) { Reclamar(arte, estado, "o .tres nao existe no disco"); return; }
		if (ResourceLoader.Load<SpriteFrames>(arte) is not { } folha)
		{ Reclamar(arte, estado, "o .tres nao carregou como SpriteFrames"); return; }

		string anim = estado.Length > 0 ? Sanear(estado) : "default";
		if (!folha.HasAnimation(anim))
		{
			string[] nomes = [.. folha.GetAnimationNames()];
			if (nomes.Length == 0) { Reclamar(arte, estado, "o SpriteFrames nao tem animacao nenhuma"); return; }
			anim = nomes[0];
		}

		if (folha.GetFrameTexture(anim, 0) is not { } quadro)
		{ Reclamar(arte, estado, $"a animacao '{anim}' nao tem quadro 0"); return; }

		Vector2 tam = quadro.GetSize();

		// ============================ O DRAGAO SE CENTRA, COMO O `center()` DO DM ============================
		// `A.center()` (`Dragonballs.dm:11-13`) faz `pixel_x = 32 - largura/2`, o que num icone de 256
		// da -96: o dragao de trinta e dois pixels de base fica com o corpo por cima do tile. Aqui e a
		// mesma conta, escrita a partir do tamanho REAL da folha em vez do numero cravado -- se a arte
		// mudar de largura, ele continua centrado.
		//
		// O LEQUE DAS ESFERAS E DO CORE (`Esferas.LequeDe`), com o Y ja invertido pro Godot. Sem ele,
		// sete esferas no mesmo tile sao um sprite so -- e quem reuniu as sete nao ve as sete.
		// ================================================================================================
		float dx, dy;
		if (Tipo == Protocol.CoisaDeEsfera.Dragao)
		{
			dx = ZoneCollision.TileSize - tam.X / 2f;
			dy = -tam.Y;
		}
		else
		{
			(float lx, float ly) = Tipo == Protocol.CoisaDeEsfera.Esfera
				? Esferas.LequeDe(Numero)
				: (0f, 0f);
			dx = lx;
			dy = -tam.Y + ly;
		}

		_sprite = new AnimatedSprite2D
		{
			SpriteFrames = folha,
			Animation = anim,
			Centered = false,
			Position = new Vector2(dx, dy),
		};
		AddChild(_sprite);
		_sprite.Play();
	}

	/// <summary>Mesmo saneamento de nome da <see cref="ObraDesenhada"/> -- as duas leem os mesmos `.tres`.</summary>
	private static string Sanear(string s)
	{
		var sb = new System.Text.StringBuilder(s.Length);
		foreach (char c in s.ToLowerInvariant()) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
		string r = sb.ToString().Trim('_');
		while (r.Contains("__")) r = r.Replace("__", "_");
		return r.Length == 0 ? "state" : r;
	}

	/// <summary>
	/// O BRILHO -- e ele NAO e enfeite: e a unica coisa na tela que separa uma esfera ACORDADA de uma
	/// que ainda se refaz.
	///
	/// O `.dmi` tem o estado "inactive", entao a esfera apagada ja se desenha diferente. O pulso
	/// dourado por cima existe pro caso oposto: a esfera VIVA no meio de um mato de 32 px de sprite,
	/// que e onde alguem passa por ela sem ver. Vale a mesma regra do "solta pisca" da construcao --
	/// descobrir o estado so ao clicar e a diferenca entre um jogo que ensina e um que esconde.
	///
	/// A RESERVA (quando falta arte) e uma bolinha dourada: melhor feia do que invisivel. Uma esfera
	/// invisivel seria indistinguivel de uma esfera que nao nasceu.
	/// </summary>
	public override void _Draw()
	{
		if (Tipo == Protocol.CoisaDeEsfera.Dragao) return;

		if (_sprite == null)
		{
			var cor = new Color(0.98f, 0.76f, 0.18f);
			DrawCircle(new Vector2(ZoneCollision.TileSize / 2f, -ZoneCollision.TileSize / 2f),
					   Tipo == Protocol.CoisaDeEsfera.Estatua ? 14f : 8f,
					   Inerte ? cor.Darkened(0.55f) : cor);
		}

		if (Inerte || Tipo != Protocol.CoisaDeEsfera.Esfera) return;

		float p = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 320f);
		(float lx, float ly) = Esferas.LequeDe(Numero);
		DrawCircle(new Vector2(ZoneCollision.TileSize / 2f + lx, -ZoneCollision.TileSize / 2f + ly),
				   13f + 3f * p, new Color(1f, 0.85f, 0.25f, 0.10f + 0.14f * p));
	}

	public override void _Process(double delta)
	{
		// SO A QUE PULSA PEDE QUADRO NOVO. A estatua e o dragao sao parados, e redesenhar os tres
		// trinta vezes por segundo seria pagar o quadro de todos por causa de um.
		if (!Inerte && Tipo == Protocol.CoisaDeEsfera.Esfera) QueueRedraw();
	}
}
