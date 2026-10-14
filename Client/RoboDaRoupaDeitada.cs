using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA ROUPA COM O CORPO NO CHAO (`--diagroupadeitada`) ============================
/// O dono (2026-10-14), com a foto de um nocaute: *"essa armadura (cloth saiyan suit) ta toda bugada"*. A
/// `ClothesSaiyanSuit` nao tem quadro de KO, e com o corpo deitado a camada dela descia a escada do `Escolher` ate um
/// quadro EM PE -- que a rotacao do node deitava por cima do corpo. A regra nova mora no `CharacterVisual.Escolher`.
///
///     &lt;godot&gt; --headless --path . --diagroupadeitada
///
///   1. A PECA DO PEDIDO, pelo `Vestir` e pelo `SetPose` de producao, junto da gemea que tem o quadro
///      (`Clothes_SaiyanSuit`): no chao a sem quadro some e a com quadro aparece deitada; de pe, as duas voltam.
///   2. O CATALOGO INTEIRO: cada roupa, vestida sozinha num corpo nocauteado, ou aparece num quadro de KO ou nao
///      aparece -- nunca num quadro em pe.
///   3. CONTRA-EXEMPLO (`CharacterVisual.RoupaEmPeNoChaoDeTeste`): com a regra de antes a armadura aparece em pe por
///      cima do corpo deitado, e o catalogo acha as outras. As duas provas sabem reprovar.
///
/// Headless nao desenha: o que se le e a camada (visivel, e em que animacao) que o caminho de producao escolheu.
/// </summary>
public partial class RoboDaRoupaDeitada : Node
{
	private const string SemQuadro = "res://Assets/Sprites/Clothes/ClothesSaiyanSuit.tres";
	private const string ComQuadro = "res://Assets/Sprites/Clothes/Clothes_SaiyanSuit.tres";
	private int _ok, _falhou;

	private readonly record struct Lida(bool Visivel, string Animacao)
	{
		public bool Deitada => Animacao.StartsWith("ko", StringComparison.Ordinal);
		public override string ToString() => Visivel ? $"visivel em '{Animacao}'" : "escondida";
	}

	private void Conferir(bool passou, string oque, string detalhe = "")
	{
		string resto = detalhe.Length > 0 ? $"   [{detalhe}]" : "";
		if (passou) { _ok++; GD.Print($"[roupadeitada]  ok      {oque}{resto}"); }
		else { _falhou++; GD.Print($"[roupadeitada]  FALHOU  {oque}{resto}"); }
	}

	public override void _Ready()
	{
		GD.Print("[roupadeitada] ==== A ROUPA SEM DESENHO DEITADO, COM O CORPO NO CHAO (dono, 2026-10-14) ====");
		const string dados = "res://Assets/Data/visual.json";
		VisualCatalog? cat = Godot.FileAccess.FileExists(dados) ? VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados)) : null;
		if (cat == null)
		{
			Conferir(false, "o catalogo visual carrega", dados);
			Sair();
			return;
		}

		try
		{
			(Lida? semNoChao, Lida? comNoChao, Lida? semDePe, Lida? comDePe) = APecaDoPedido(cat);
			Conferir(semNoChao is { Visivel: false },
					 "**A `ClothesSaiyanSuit` (SEM QUADRO DEITADO) SOME COM O CORPO NO CHAO** -- nada em pe por cima do corpo deitado",
					 $"{semNoChao}");
			Conferir(comNoChao is { Visivel: true, Deitada: true },
					 "...e a gemea `Clothes_SaiyanSuit`, que tem o quadro, aparece DEITADA no mesmo corpo", $"{comNoChao}");
			Conferir(semDePe is { Visivel: true, Deitada: false } && comDePe is { Visivel: true, Deitada: false },
					 "de pe de novo, as duas voltam: a peca so some enquanto ele esta no chao", $"{semDePe} / {comDePe}");

			(List<string> emPe, int somem, int deitadas, int naoVestiu) = OCatalogo(cat);
			Conferir(emPe.Count == 0 && naoVestiu == 0 && somem + deitadas == cat.Roupas.Count,
					 $"**NENHUMA das {cat.Roupas.Count} roupas do catalogo aparece EM PE num corpo nocauteado**: {deitadas} deitam com ele, {somem} (sem desenho deitado) somem",
					 $"em pe: {string.Join(", ", emPe.Take(8))}; nao vestiram: {naoVestiu}");
			Conferir(somem > 0 && somem < cat.Roupas.Count / 4,
					 "(montagem) as que somem sao as poucas sem quadro de KO, e nao o guarda-roupa inteiro", $"{somem} de {cat.Roupas.Count}");

			CharacterVisual.RoupaEmPeNoChaoDeTeste = true;
			(Lida? semInjetado, _, _, _) = APecaDoPedido(cat);
			Conferir(semInjetado is { Visivel: true, Deitada: false },
					 "(defeito injetado: a regra de antes) **A ARMADURA APARECE EM PE POR CIMA DO CORPO DEITADO**. A prova 1 sabe reprovar",
					 $"{semInjetado}");
			(List<string> emPeInjetado, _, _, _) = OCatalogo(cat);
			Conferir(emPeInjetado.Count > 0 && emPeInjetado.Any(p => p.StartsWith("ClothesSaiyanSuit ", StringComparison.Ordinal)),
					 "(defeito injetado) ...e o catalogo acha a armadura e as outras em pe. A prova 2 sabe reprovar",
					 $"{emPeInjetado.Count} em pe: {string.Join(", ", emPeInjetado.Take(6))}");
		}
		catch (Exception e)
		{
			Conferir(false, "a bancada rodou ate o fim", e.ToString());
		}
		finally
		{
			CharacterVisual.RoupaEmPeNoChaoDeTeste = false;
		}

		GD.Print(_falhou == 0
			? $"[roupadeitada] ===== TUDO OK ({_ok} provas) ====="
			: $"[roupadeitada] ===== {_falhou} FALHA(S) em {_ok + _falhou} provas =====");
		Sair();
	}

	private void Sair() => GetTree().Quit(_falhou == 0 ? 0 : 1);

	/// <summary>Um corpo de verdade (Saiyajin, homem) vestido com estas pecas pelo `Vestir` de producao.</summary>
	private CharacterVisual Vestido(VisualCatalog cat, params string[] pecas)
	{
		var corpo = new CharacterVisual { Name = "Vestido" };
		AddChild(corpo);
		corpo.Vestir(cat, new Appearance { Cabelo = "Goku", Roupa = [.. pecas.Select(p => new PecaDeRoupa(p))] }, "Saiyan", "Male");
		return corpo;
	}

	/// <summary>A camada desta peca no corpo: visivel, e em que animacao. Nulo se ela nao foi vestida.</summary>
	private static Lida? Camada(CharacterVisual corpo, string peca)
	{
		int i = corpo.RoupasNoCorpoDeTeste().IndexOf(peca);
		if (i < 0) return null;
		foreach ((string nome, AnimatedSprite2D s) in corpo.CamadasDeTeste())
			if (nome == $"roupa{i}") return new Lida(s.Visible, s.Animation.ToString());
		return null;
	}

	/// <summary>As duas Saiyan Suit no mesmo corpo: as camadas delas com ele nocauteado e, depois, de pe.</summary>
	private (Lida? SemNoChao, Lida? ComNoChao, Lida? SemDePe, Lida? ComDePe) APecaDoPedido(VisualCatalog cat)
	{
		CharacterVisual corpo = Vestido(cat, SemQuadro, ComQuadro);
		try
		{
			corpo.SetPose(Protocol.Pose.Nocauteado);
			Lida? semNoChao = Camada(corpo, SemQuadro), comNoChao = Camada(corpo, ComQuadro);
			corpo.SetPose(Protocol.Pose.Normal);
			return (semNoChao, comNoChao, Camada(corpo, SemQuadro), Camada(corpo, ComQuadro));
		}
		finally { corpo.QueueFree(); }
	}

	/// <summary>Cada roupa do catalogo, sozinha num corpo nocauteado: em pe (o defeito), escondida, ou deitada.</summary>
	private (List<string> EmPe, int Somem, int Deitadas, int NaoVestiu) OCatalogo(VisualCatalog cat)
	{
		var emPe = new List<string>();
		int somem = 0, deitadas = 0, naoVestiu = 0;
		foreach (string peca in cat.Roupas)
		{
			CharacterVisual corpo = Vestido(cat, peca);
			corpo.SetPose(Protocol.Pose.Nocauteado);
			Lida? l = Camada(corpo, peca);
			if (l is not { } lida) naoVestiu++;
			else if (!lida.Visivel) somem++;
			else if (lida.Deitada) deitadas++;
			else emPe.Add($"{peca.GetFile().GetBaseName()} ({lida.Animacao})");
			corpo.QueueFree();
		}
		return (emPe, somem, deitadas, naoVestiu);
	}
}
