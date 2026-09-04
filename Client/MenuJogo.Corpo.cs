using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA BODY -- o corpo por membros: o boneco do HUD em DOBRO a esquerda e, a direita, um cartao por
/// regiao (cabeca, tronco, bracos, pernas, rabo) com uma barra por membro, na cor da faixa de vida.
///
/// E o `ui_tab_body` do original (`HtmlUI.dm:302-311`): uma linha por membro com a porcentagem e a
/// PALAVRA de ferimento (`injury_word`, `HtmlUI.dm:313-319`) -- as mesmas seis faixas que o boneco
/// pinta (<see cref="BodyDoll.Cor"/>), pra quem le a lista e quem olha o desenho lerem a mesma coisa.
///
/// O DM tirava da lista o membro arrancado (`if(b.lopped || b.status == "Missing") continue`, `:306`) e
/// o deixava so no roxo do boneco. Aqui ele FICA na lista, com a pilula DECEPADO: sumir da lista e
/// exatamente o que um jogador que nao olha o boneco nao tem como notar.
///
/// OS NOMES DO FIO VEM SEM ACENTO (`ParteState.Nome` e a chave que o servidor, o boneco e a assinatura de
/// cache usam), e o fio NAO muda: a traducao e daqui pra tela, num mapa so (<see cref="NomeBonitoDoMembro"/>).
/// </summary>
public partial class MenuJogo
{
	/// <summary>
	/// AS REGIOES, NA ORDEM DO CORPO (de cima pra baixo), e os membros do fio que cada uma resume. A
	/// ordem e a da arvore de nodes -- mesma ficha, mesma arvore. Um membro que nao esteja em grupo
	/// nenhum (uma raca nova) cai num cartao "Outros" em vez de sumir.
	/// </summary>
	private static readonly (string Regiao, string[] Membros)[] RegioesDoCorpo =
	[
		("Cabeça", ["Cabeca", "Cerebro"]),
		("Tronco", ["Torso", "Abdomen", "Orgaos", "Reprodutor"]),
		("Braços", ["Braco esquerdo", "Mao esquerda", "Braco direito", "Mao direita"]),
		("Pernas", ["Perna esquerda", "Pe esquerdo", "Perna direita", "Pe direito"]),
		("Rabo", ["Rabo"]),
	];

	/// <summary>O nome do fio (sem acento) -> o nome que a tela escreve. Quem nao esta no mapa sai como veio.</summary>
	internal static string NomeBonitoDoMembro(string nome) => nome switch
	{
		"Cabeca" => "Cabeça",
		"Cerebro" => "Cérebro",
		"Abdomen" => "Abdômen",
		"Orgaos" => "Órgãos",
		"Braco esquerdo" => "Braço esquerdo",
		"Braco direito" => "Braço direito",
		"Mao esquerda" => "Mão esquerda",
		"Mao direita" => "Mão direita",
		"Pe esquerdo" => "Pé esquerdo",
		"Pe direito" => "Pé direito",
		_ => nome,
	};

	/// <summary>
	/// O `injury_word` do DM (`HtmlUI.dm:313-319`), em portugues. Os cortes sao os MESMOS do
	/// <see cref="BodyDoll.Cor"/> (100/80/60/40/20): a palavra e a cor da barra dizem sempre a mesma faixa.
	/// </summary>
	internal static string PalavraDeFerimento(int pct) => pct switch
	{
		>= 100 => "Saudável",
		>= 80 => "Levemente ferido",
		>= 60 => "Ferido",
		>= 40 => "Gravemente ferido",
		>= 20 => "Crítico",
		_ => "Quebrado",
	};

	private void Corpo()
	{
		List<Protocol.ParteState> partes = GameClient.Instance?.Corpo ?? [];
		if (partes.Count == 0) { Aviso("O corpo ainda não chegou do servidor."); return; }
		SheetState f = GameClient.Instance?.Sheet ?? default;

		// A FAIXA: a vida inteira (a media dos membros, que e o que o HUD chama de VIDA) e o resumo do
		// que a lista de baixo detalha -- quantos membros nao estao inteiros, quantos nao existem mais.
		int feridos = partes.Count(p => !p.Decepado && p.Vida < 100);
		int decepados = partes.Count(p => p.Decepado);
		Faixa("Vida", $"{f.HP:0}%", $"{feridos} membros feridos · {decepados} decepados", CorDaRazao(f.HP / 100));

		// DUAS COLUNAS DESIGUAIS: o boneco so precisa da largura dele; o resto e da lista. Um
		// `Colunas()` dividiria ao meio e deixaria metade da pagina vazia em volta de um desenho de 192 px.
		var linha = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		linha.AddThemeConstantOverride("separation", 8);
		linha.Alignment = BoxContainer.AlignmentMode.Begin;
		_conteudo.AddChild(linha);

		VBoxContainer esquerda = Cartao("Corpo", linha);
		if (esquerda.GetParent() is PanelContainer cartaoDoBoneco)
		{
			cartaoDoBoneco.SizeFlagsHorizontal = Control.SizeFlags.Fill;   // so a largura que o boneco pede
			cartaoDoBoneco.SizeFlagsVertical = Control.SizeFlags.Fill;
		}
		esquerda.AddChild(BonecoEmDobro());
		Nota("cada região na cor da vida do próprio membro; roxo é o que foi arrancado.", esquerda);

		var direita = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		direita.AddThemeConstantOverride("separation", 8);
		linha.AddChild(direita);

		var porNome = new Dictionary<string, Protocol.ParteState>(partes.Count);
		foreach (Protocol.ParteState p in partes) porNome[p.Nome] = p;
		var vistos = new HashSet<string>();
		foreach ((string regiao, string[] membros) in RegioesDoCorpo)
		{
			List<string> presentes = membros.Where(porNome.ContainsKey).ToList();
			if (presentes.Count == 0) continue;   // humano nao tem rabo: o cartao nao nasce
			VBoxContainer cartao = Cartao(regiao, direita);
			foreach (string m in presentes) { LinhaDeMembro(porNome[m], cartao); vistos.Add(m); }
		}
		List<Protocol.ParteState> sobras = partes.Where(p => !vistos.Contains(p.Nome)).ToList();
		if (sobras.Count > 0)
		{
			VBoxContainer outros = Cartao("Outros", direita);
			foreach (Protocol.ParteState p in sobras) LinhaDeMembro(p, outros);
		}
	}

	/// <summary>
	/// O BONECO DO HUD, EM DOBRO. A arte e 96x96 (`BodyDoll.Escala` = 1, o que cabe no canto do HUD); na
	/// aba ha espaco, e o dobro e o que deixa as regioes distinguiveis de relance. A MOLDURA reserva o
	/// tamanho FINAL (192x192): container do Godot ignora `Scale` no layout, entao sem ela o boneco dobrado
	/// invadiria o cartao vizinho. O boneco se assina em `CorpoAtualizado` sozinho (no `_Ready` dele) e se
	/// desassina ao sair -- a pagina pode ser destruida e remontada a vontade.
	/// </summary>
	private static Control BonecoEmDobro()
	{
		var moldura = new Control
		{
			CustomMinimumSize = new Vector2(96 * BodyDoll.Escala * 2, 96 * BodyDoll.Escala * 2),
			MouseFilter = Control.MouseFilterEnum.Ignore,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
		};
		moldura.AddChild(new BodyDoll { Scale = new Vector2(2, 2) });
		return moldura;
	}

	/// <summary>Uma linha por membro: barra na cor da faixa, "NN%   ·   palavra"; o decepado vira pilula.</summary>
	private static void LinhaDeMembro(Protocol.ParteState p, Control pai)
	{
		string nome = NomeBonitoDoMembro(p.Nome);
		if (p.Decepado) { pai.AddChild(LinhaComPilula(nome, "DECEPADO", BodyDoll.CorDecepado)); return; }
		Color cor = BodyDoll.Cor(p.Vida);
		LinhaComBarra(nome, $"{p.Vida}%   ·   {PalavraDeFerimento(p.Vida)}", p.Vida / 100.0, cor, pai, corDoValor: cor);
	}

	/// <summary>
	/// ROTULO A ESQUERDA E UMA PILULA A DIREITA: o estado que nao e numero (DECEPADO). Carrega o metadado
	/// `linha` como as outras linhas. E uma peca da lingua comum que ainda nao esta em `MenuJogo.Pecas.cs`
	/// (arquivo compartilhado, que esta frente nao edita) -- candidata a subir pra la.
	/// </summary>
	private static HBoxContainer LinhaComPilula(string rotulo, string estado, Color cor)
	{
		var h = new HBoxContainer();
		h.SetMeta("linha", rotulo);
		var a = new Label { Text = rotulo, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		a.AddThemeColorOverride("font_color", Tema.TextoFraco);
		a.AddThemeFontSizeOverride("font_size", 13);
		h.AddChild(a);
		PanelContainer p = Pilula(estado, cor);
		p.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		h.AddChild(p);
		return h;
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`): a basica ja cobre nome, vida
	/// e decepado de cada membro; a faixa desenha a VIDA INTEIRA, que muda por outro caminho (a media
	/// arredondada pode mexer sem que a lista de 4% em 4% do corpo mexa), entao ela entra aqui.
	/// </summary>
	private string ExtraDaAssinaturaDeCorpo(SheetState f) => $"{f.HP:0}";
}
