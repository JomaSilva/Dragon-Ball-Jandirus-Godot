using Godot;
using Jandirus.Core.Skills;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA STATS -- o `ui_tab_stats` do original (`HtmlUI.dm:175-229`): poder, vitais, atributos,
/// treino e estado. Cada aba mora no proprio arquivo (e e a propria frente de trabalho); as pecas
/// comuns em `MenuJogo.Pecas.cs`.
///
/// ============================ O REDESENHO (2026-09-03) ============================
/// O dono: *"ta mt cru o resto, da uma boa melhorada pra deixar mais profissional"*. A aba era uma
/// coluna de "rotulo ..... valor" com quatro titulos de secao e muros de texto. Agora ela fala a
/// lingua da Learning (`MenuJogo.Pecas.cs`):
///
///   * a FAIXA com o Battle Power no topo -- o unico numero que se le primeiro, e o UNICO lugar do
///     menu inteiro com licenca de imprimir BP (ver o sigilo em <see cref="Stats"/>);
///   * quatro CARTOES em duas colunas: Vitais (barras, as mesmas do HUD), Atributos (barras contra
///     o maior deles, com alto/medio/baixo), Treino (o multiplicador e as partes dele em pilulas;
///     acende em laranja quando o esmagamento prende no chao) e Estado (raca, idade, condicao e
///     golpe em pilulas, cadencia, estilo, marcos, zeni, lugar);
///   * os muros de texto viraram UMA nota curta por cartao, e o resto foi pro tooltip.
///
/// ============================ O CONTRATO COM AS BANCADAS NAO MUDOU ============================
/// A `--diagbancada` le `ValorDesenhado("Stats", ...)` de "Battle Power" ("???   (sem scouter)" ou
/// "N   (base N)"), "Ki" ("120 / 120   (100%)"), "Nutrição" ("50 / 50   (100%)") e "Poder efetivo"
/// ("98%   (...)"), e desmonta esses textos por regex. Os rotulos e a FORMA dos valores continuam
/// os mesmos -- so a moldura em volta mudou (a faixa le como linha: ver `Faixa`). A `--diagabas`
/// (F2, `RoboDasAbas.Stats.cs`) cobra isso por texto, por barra, por pilula e por pixel.
/// ============================================================================================
/// </summary>
public partial class MenuJogo
{
	// =====================================================================
	// STATS -- o ui_tab_stats do original
	// =====================================================================
	private void Stats(SheetState f)
	{
		GameClient? cli = GameClient.Instance;

		// "???" SEM SCOUTER. E a regra do original e ela e do JOGO, nao da interface: ninguem
		// le o proprio poder de luta em numero sem um aparelho que meca.
		//
		// COM scouter sai o expresso E o base, e as duas metades sao literais do DM
		// (HtmlUI.dm:178-181: `[FullNum(round(expressedBP))] (base [FullNum(round(BP))])` no ramo
		// `if(scouteron)`, `??? (no scouter)` no outro). Esta e a UNICA linha do painel inteiro que
		// tem permissao de imprimir BP -- a aba Forms nao imprime nem o limiar (ver AbaFormas).
		//
		// E A FAIXA, e nao uma linha comum: e o numero da aba. O "???" sai apagado de proposito --
		// e uma ausencia, nao um valor -- e o numero de verdade sai no laranja da casa. A porta de
		// bancada le a faixa como "grande + legenda", que e exatamente o texto de antes.
		bool scouter = _atributos.Tem(Protocol.Poder.Scouter);
		PanelContainer faixa = scouter
			? Faixa("Battle Power", Lido(f.ExpressedBP), $"(base {Lido(f.BP)})", double.IsNaN(f.ExpressedBP) ? Tema.TextoFraco : null)
			: Faixa("Battle Power", "???", "(sem scouter)", Tema.TextoFraco);
		faixa.TooltipText = DicaDaFaixaDeBp(scouter, f);

		// COM O SCOUTER A FAIXA E VIVA (2026-10-19): o BP sobe a cada tique de treino, e o numero se reescreve no lugar
		// em vez de remontar a aba inteira (ver `Viva`). Sem ele ela e o "???" parado -- nada a reescrever.
		if (scouter)
		{
			(Label grande, Label legenda) = PartesDaFaixa(faixa);
			Color corDoGrande = double.IsNaN(f.ExpressedBP) ? Tema.TextoFraco : Tema.Destaque;
			Viva(() =>
			{
				SheetState s = GameClient.Instance?.Sheet ?? default;
				grande.Text = Lido(s.ExpressedBP);
				legenda.Text = $"(base {Lido(s.BP)})";
				Color cor = double.IsNaN(s.ExpressedBP) ? Tema.TextoFraco : Tema.Destaque;
				if (cor != corDoGrande) { grande.AddThemeColorOverride("font_color", cor); corDoGrande = cor; }
				faixa.TooltipText = DicaDaFaixaDeBp(true, s);
			});
		}

		// A ORDEM E POR ALTURA E POR ASSUNTO. A grade da cada linha a altura do cartao mais alto dela,
		// entao os pares tem que ter alturas parecidas: Vitais (cinco barras) com Treino em cima --
		// os dois sao "como estou AGORA" --, e Atributos (oito barras) com Estado (nove linhas) embaixo
		// -- os dois sao "quem eu sou". Com Atributos ao lado de Vitais sobrava um terco de cartao vazio.
		GridContainer grade = Colunas();
		Vitais(f, grade);
		Treino(grade);
		Atributos(grade);
		Estado(f, cli, grade);
	}

	/// <summary>
	/// (2026-10-18) COM O SCOUTER, O QUE O VIDRO NAO ALCANCA CHEGA NaN: acima de 100 milhoes, ou o ki divino do proprio
	/// dono -- e a faixa escreve "???" (`Core/Stats/Scouter.cs`).
	/// </summary>
	private static string Lido(double bp) => double.IsNaN(bp) ? "???" : $"{bp:N0}";

	/// <summary>A dica da faixa do BP: sem scouter, com ele, e com ele estourado. Uma conta pra montagem e pra viva.</summary>
	private static string DicaDaFaixaDeBp(bool scouter, SheetState f) =>
		!scouter
			? "Ninguém lê o próprio poder de luta sem um aparelho que meça. Ligue um scouter na aba Equip."
			: double.IsNaN(f.ExpressedBP) || double.IsNaN(f.BP)
				? "O scouter não lê poder acima de 100 milhões, nem ki divino: o que ele não alcança aparece como ???."
				: "O poder que o mundo lê (expresso) e o que o treino sobe (base). Só existe em número com o scouter ligado.";

	// =====================================================================
	// VITAIS -- as barras do HUD, lidas dos MESMOS campos
	// =====================================================================
	private void Vitais(SheetState f, Control pai)
	{
		VBoxContainer c = Cartao("Vitais", pai);

		// ============================ QUANTO DISSO ESTA SAINDO ============================
		// A MESMA leitura que o HUD poe ao lado do BP, do MESMO campo -- e por isso as duas telas nao
		// tem como divergir. Aparece com scouter ou sem: e razao, nao poder (ver `GameServer.Sigilo`).
		//
		// A frase explica o que ela nao e: transformar multiplica os dois lados da fracao, entao a
		// forma nao mexe nela. Quem mexe e Ki, ferimento, peso, gravidade e idade. Vem PRIMEIRO no
		// cartao porque e o resumo das quatro barras abaixo.
		VitalVivo("Poder efetivo", c, f, s =>
		{
			Color corDoEfetivo = CorDaRazao(s.Inteireza, bom: 0.9, ruim: 0.5);
			return new Vital($"{s.Inteireza * 100:0.#}%   (do seu pico sem desgaste)", s.Inteireza, corDoEfetivo, corDoEfetivo);
		});

		VitalVivo("Vida", c, f, s => new Vital($"{s.HP:0}%", s.HP / 100, Tema.Vida, CorDaRazao(s.HP / 100)));

		// A RAZAO SAI DO STRUCT, e nao de um `Ki / MaxKi` escrito aqui: era essa copia -- tres delas,
		// nesta aba, na aba Ki e no HUD -- que deixava o corte de uma passar despercebido nas outras.
		// A barra tem o trilho do teto de carga (`TrilhoDeKi`), como a do HUD: acima do tanque ela
		// continua crescendo, na cor de excesso, e o texto acende -- e o power-up funcionando.
		VitalVivo("Ki", c, f, s =>
		{
			bool excesso = s.RazaoDeKi > 1;
			return new Vital($"{s.Ki:N0} / {s.MaxKi:N0}   ({s.RazaoDeKi * 100:0}%)", s.RazaoDeKi,
				excesso ? Tema.KiExcesso : Tema.Ki, excesso ? Tema.Destaque : Tema.Texto, s.TrilhoDeKi);
		});

		// O VIGOR VIVO da ficha rapida (`stamina / maxstamina`, o mesmo que a barra do HUD desenha),
		// e nao o `Stamina` da ficha lenta que esta linha lia ate 2026-09-03. Duas telas lendo campos
		// diferentes pra mesma barra e o defeito que ja mordeu o Ki; e a ficha lenta nem entrava na
		// assinatura da pagina, entao o numero podia ficar congelado.
		VitalVivo("Vigor", c, f, s => new Vital($"{s.RazaoDeVigor * 100:0}%", s.RazaoDeVigor, Tema.Vigor, null));

		// ============================ A NUTRICAO EXPLICA O VIGOR ============================
		// O vigor cai sozinho e so sobe as custas do tanque de comida. Sem este numero na tela, um
		// jogador com o folego minguando nao tem como saber que o problema e FOME -- ele ve uma
		// barra caindo e nenhuma causa. Ver `Core.Stats.Nutricao`.
		//
		// A COR AVISA ANTES DE DOER: o aviso de fome do servidor bate em 25% de vigor, mas quem fica
		// sem tanque para de recuperar MUITO antes disso. A barra do HUD le esta mesma razao.
		VitalVivo("Nutrição", c, f, s =>
		{
			double pct = s.RazaoDeNutricao * 100;
			return new Vital($"{s.Nutricao:0} / {s.NutricaoMax:0}   ({pct:0}%)", s.RazaoDeNutricao,
				Tema.Nutricao, pct >= 50 ? Tema.Bom : pct <= 15 ? Tema.Perigo : Tema.Texto);
		});

		Nota("O poder efetivo cai com Ki baixo, ferimento, fome, peso, gravidade e idade. Transformar não mexe nele.", c);
	}

	/// <summary>O que uma linha dos vitais escreve: o valor, a barra (razao e cor), a cor do valor e o teto da barra.</summary>
	private readonly record struct Vital(string Texto, double Razao, Color Barra, Color? Valor, double Teto = 1);

	/// <summary>
	/// UMA LINHA DOS VITAIS, montada com a ficha de agora e registrada como VIVA pela MESMA leitura (2026-10-19). Em
	/// luta os cinco numeros mudam a cada ficha, e reescreve-los no lugar e o que tirou a Stats das cinco remontagens
	/// por segundo (ver `Viva`, MenuJogo.cs). Uma leitura so pras duas pontas: a linha que nasce e a que se reescreve
	/// nao tem como divergir.
	/// </summary>
	private void VitalVivo(string rotulo, Control pai, SheetState f, Func<SheetState, Vital> ler)
	{
		Vital v = ler(f);
		LinhaViva linha = LinhaComBarra(rotulo, v.Texto, v.Razao, v.Barra, pai, v.Valor, v.Teto);
		Viva(() =>
		{
			Vital agora = ler(GameClient.Instance?.Sheet ?? default);
			linha.Escrever(agora.Texto, agora.Razao, agora.Barra, agora.Valor, agora.Teto);
		});
	}

	// =====================================================================
	// ATRIBUTOS -- os oito do original, em barras contra o MAIOR deles
	// =====================================================================
	private void Atributos(Control pai)
	{
		VBoxContainer c = Cartao("Atributos", pai);

		(string Nome, float Valor)[] atts =
		[
			("Ofensiva Física", _atributos.PhysOff), ("Defesa Física", _atributos.PhysDef),
			("Ofensiva de Ki", _atributos.KiOff),    ("Defesa de Ki", _atributos.KiDef),
			("Técnica", _atributos.Technique),       ("Perícia de Ki", _atributos.KiSkill),
			("Velocidade", _atributos.Speed),        ("Esotérico", _atributos.Esoteric),
		];

		// A COR SAI DA COMPARACAO COM A PROPRIA MEDIA, como no `ui_qual()` (HtmlUI.dm:87-96): um
		// atributo nao e alto ou baixo em absoluto, e alto ou baixo PRA ESTE personagem. E o que
		// deixa a vocacao de cada um visivel de relance.
		//
		// A BARRA E CONTRA O MAIOR ATRIBUTO, e nao contra um teto absoluto: nao existe "atributo
		// maximo" no jogo, e o que se quer ver e o PERFIL -- qual e o forte e quanto os outros
		// ficam atras dele. A cor da barra e a da qualidade; a do meio sai apagada pra que verde e
		// vermelho saltem.
		float media = 0, maior = 0;
		foreach ((string _, float v) in atts) { media += v; maior = Math.Max(maior, v); }
		media /= Math.Max(atts.Length, 1);

		foreach ((string nome, float v) in atts)
		{
			(string rotulo, Color cor) = QualidadeDoAtributo(v, media);
			Color barra = rotulo == "médio" ? Tema.TextoFraco : cor;
			LinhaComBarra(nome, $"{v * 10:0}   ({rotulo})", maior > 0 ? v / maior : 0, barra, c, cor);
		}
		Linha("Força de Vontade", $"{_atributos.Willpower:0.##}", null, c);

		Nota("Alto, médio e baixo comparam com a SUA média: é a vocação, não o poder.", c);
	}

	/// <summary>
	/// "alto" / "médio" / "baixo" e a cor, como o `ui_qual()` / `ui_qual_label()` do original
	/// (HtmlUI.dm:87-96): 20% acima da media e alto, 20% abaixo e baixo. E a regra que a F2 da
	/// `--diagabas` copia -- e inverte -- pra provar que a prova dela sabe reprovar.
	/// </summary>
	private static (string Rotulo, Color Cor) QualidadeDoAtributo(float v, float media) =>
		v >= media * 1.2f ? ("alto", Tema.Bom)
		: v <= media * 0.8f ? ("baixo", Tema.Perigo)
		: ("médio", Tema.Texto);

	// =====================================================================
	// TREINO -- a linha "BP GAIN" do painel do original (`HtmlUI.dm:182-187`)
	// =====================================================================
	/// <summary>
	/// QUANTO O TREINO ESTA RENDENDO.
	///
	/// ============================ ESTA SECAO E O SISTEMA ============================
	/// Peso, gravidade e Sala do Tempo mudam quanto BP entra por tique. BP por tique nao se ve: o
	/// numero da tela e o mesmo antes e depois, e so muda mais rapido -- o que, num jogo onde o BP
	/// sobe a vida inteira, e indistinguivel de nada acontecendo. A queixa que abriu esta camada e
	/// literalmente essa ("o sistema e invisivel e parece nao existir").
	///
	/// Por isso o cartao nao mostra so o produto: ele mostra as PARTES, em pilulas -- "10x grav",
	/// "1,4x pesos", "Sala 280x" -- que e uma frase acionavel: diz o que aumentar. "2800x" sozinho e
	/// um numero magico. Eram o parenteses da linha; viraram pilulas porque o parenteses inteiro nao
	/// cabe na metade de uma pagina quando as quatro partes aparecem juntas, e em pilula cada parte
	/// tem cor propria e quebra linha sozinha. A frase de antes continua inteira no tooltip da linha.
	///
	/// O NUMERO VEM PRONTO DO SERVIDOR (`Fighter.MultiplicadorDeGanho`, no Core). O cliente nao refaz
	/// a conta: ele nao tem `Egains`, nem `GravMastered`, nem `zoneGainMult`, e uma segunda copia da
	/// formula seria a que envelhece calada.
	/// =============================================================================
	/// </summary>
	private void Treino(Control pai)
	{
		// ============================ O ESMAGAMENTO E O OUTRO LADO DA MESMA MOEDA ============================
		// A razao acima de 1 e o que a gravidade alta (ou o peso) COBRA: dano por segundo, folego
		// drenado e passo mais lento. Ela fica no mesmo cartao do ganho de proposito -- as duas linhas
		// juntas sao a decisao inteira ("rende 10x e me machuca 1,8x do que aguento").
		//
		// A partir de `RazaoQuePrende` o corpo nao anda. Sem esta linha, um jogador preso no chao teria
		// as teclas mortas e nenhuma explicacao na tela -- por isso o cartao INTEIRO acende em laranja
		// nesse caso: e o primeiro lugar pra onde o olho vai.
		// ==================================================================================================
		double r = _atributos.Esmagamento;
		bool preso = r >= Jandirus.Core.Stats.Esmagamento.RazaoQuePrende;
		VBoxContainer c = Cartao("Treino", pai, destaque: preso);

		var partes = new List<(string Texto, Color Cor)>();
		if (_atributos.Gravidade > 0) partes.Add(($"{_atributos.Gravidade:0.#}x grav", Tema.Texto));
		// (A ACLIMATACAO NAO ENTRA AQUI desde 2026-10-16: ela e PODER, e nao treino -- a linha dela mora na aba de Formas.)
		float vigor = _atributos.VigorMult;
		if (vigor >= 1.02f) partes.Add(($"{vigor:0.##}x vigor", Tema.Bom));
		else if (vigor > 0 && vigor <= 0.98f) partes.Add(($"{vigor:0.##}x vigor", Tema.Perigo));
		if (_atributos.PesoMult > 1.001f) partes.Add(($"{_atributos.PesoMult:0.##}x pesos", Tema.Destaque));
		if (_atributos.ZonaMult > 1.001f) partes.Add(($"Sala {_atributos.ZonaMult:0}x", Tema.Destaque));
		else if (_atributos.ZonaMult < 0.999f) partes.Add(($"zona {_atributos.ZonaMult:0.##}x", Tema.Perigo));

		double g = _atributos.GanhoDeTreino;
		HBoxContainer ganho = Linha("Ganho de BP", $"{g:0.##}x", g >= 1.5 ? Tema.Bom : g < 0.9 ? Tema.Perigo : Tema.Texto, c);
		ganho.TooltipText = (partes.Count > 0
			? $"{g:0.##}x   ({string.Join(" · ", partes.Select(p => p.Texto))})"
			: $"{g:0.##}x")
			+ "   -- gravidade, peso, zona e vigor. O 1x do vigor é o tanque pela metade; cheio (a partir de 62,5%) vale 1,25x, e no fundo 0,5x";
		if (partes.Count > 0) c.AddChild(Pilulas([.. partes]));

		// ============================ A SESSAO DA SALA DO TEMPO ============================
		// A sala e a unica coisa deste jogo que tem PRAZO com castigo no fim (a porta tranca aos 50
		// minutos e so o Guardiao solta). Um prazo cujo unico sinal e uma frase que ja rolou pra
		// cima no chat e uma armadilha -- e esta linha e onde se olha pra saber quanto falta.
		//
		// O NUMERO VEM PRONTO, como o de cima: quem conta o tempo (em DIAS in-game, por tique) e o
		// servidor. O cliente nao tem o relogio do mundo dele nem sabe quando a janela foi armada.
		// ================================================================================
		switch (_atributos.SalaFase)
		{
			case 1:
				Linha("Sessão na Sala", $"~{_atributos.SalaMinutos:0} min de treino restantes",
					_atributos.SalaMinutos <= 5 ? Tema.Perigo : Tema.Bom, c);
				break;
			case 2:
				Linha("Sessão na Sala", $"ACABOU -- {_atributos.SalaMinutos:0.#} min pra sair pela porta", Tema.Perigo, c);
				break;
			case 3:
				Linha("Sessão na Sala", "PRESO -- só o Guardião da Terra (ou um admin) solta", Tema.Perigo, c);
				break;
		}

		if (r > 1.0001f)
			Linha("Esmagamento", preso
					? $"{r:0.##}x do seu limite   (PRESO NO CHÃO)"
					: $"{r:0.##}x do seu limite   (perdendo vida e velocidade)",
				Tema.Perigo, c);

		Nota(preso
			? "Acima de 4x do seu limite o corpo não anda: tire os pesos (aba Equip) ou saia deste chão."
			: "Gravidade, pesos e a Sala do Tempo multiplicam o que cada tique de treino rende. Acima do que o corpo aguenta, o chão esmaga: perde vida e velocidade, e a 4x prende.", c);
	}

	// =====================================================================
	// ESTADO -- quem eu sou e como estou (STATE + PROGRESS & CURRENCY do original, HtmlUI.dm:219-228)
	// =====================================================================
	private void Estado(SheetState f, GameClient? cli, Control pai)
	{
		VBoxContainer c = Cartao("Estado", pai);

		// A CLASSE NAO APARECE, NUNCA. Ela e sorteio cego na criacao (por isso a tela de criacao
		// so da uma dica indireta, CreationScreen.cs:500) e o painel do original tambem nunca a
		// imprimiu: `ui_tab_stats()` lista poder, atributos, emocao e estilo -- classe nao esta la
		// (HtmlUI.dm:175-229). Escrever "Legendary" numa linha entrega de graca o que o jogo inteiro
		// trata como descoberta, e ainda vaza pra quem olha a tela de outro.
		// PELO NOME DO JOGO, e nao pela chave do `races.json` -- ver `NomeDaRaca`: "Icer" e "Frost Demon".
		Linha("Raça", Jandirus.Core.Races.NomeDaRaca.Bonito(_atributos.Raca ?? ""), null, c);
		Linha("Idade", $"{_atributos.Idade}", null, c);

		// CONDICAO E GOLPE SAO PILULAS: "NOCAUTEADO" e "LETAL" se leem sem ler -- e sao os dois
		// estados que mudam o que as teclas fazem.
		LinhaComPilula("Condição", f.Morto ? "MORTO" : f.KO ? "NOCAUTEADO" : "de pé",
			f.Morto || f.KO ? Tema.Perigo : Tema.Bom, c);
		LinhaComPilula("Golpe", f.Letal ? "LETAL" : "não-letal", f.Letal ? Tema.Perigo : Tema.Texto, c);
		// O `relaxedstate` do original, no lugar onde ele o escreve (`[Emotion] / [relaxedstate]`, HtmlUI.dm:221):
		// "usando todo o poder", "usando parte do poder (40%)", "relaxado", "escondendo o poder". Ver `MenuJogo.PoderEmUso.cs`.
		LinhaDoPoderEmUso(c);

		// A CADENCIA, O ZENI E OS RECURSOS SAO VIVOS (2026-10-19): a cadencia anda com a carga de Ki e a carteira a
		// cada segundo de mineracao -- tres textos reescritos no lugar, e nao a aba inteira (ver `Viva`).
		Label cadencia = Linha("Cadência do soco", CadenciaDoSoco(f), null, c).GetChild<Label>(1);

		// O ESTILO PELO NOME, e nao pelo id: o pacote de estilos traz os dois, e o id e nome cru.
		Linha("Estilo de luta", NomeDoEstilo(cli), null, c);

		// MARCOS, ZENI E LUGAR eram a secao "PROGRESS & CURRENCY" do original. O lugar e o NOME da
		// zona, e nao "[x], [y], [z]": coordenada nao diz nada a ninguem; "Namek" diz.
		Linha("Marcos", $"{_livro.MarcosLivres} livres · {_livro.MarcosTotais} na vida", null, c);
		Label zeni = Linha("Zeni", ZeniDaStats(cli), null, c).GetChild<Label>(1);
		// OS RECURSOS AO LADO DO ZENI: sao a outra carteira (a materia-prima que se minera e que paga
		// tecnologia e base ao dobro do preco), e chegam no mesmo pacote que ele (`S2C.Carteira`).
		Label recursos = Linha("Recursos", RecursosDaStats(cli), null, c).GetChild<Label>(1);
		Viva(() =>
		{
			cadencia.Text = CadenciaDoSoco(GameClient.Instance?.Sheet ?? default);
			zeni.Text = ZeniDaStats(GameClient.Instance);
			recursos.Text = RecursosDaStats(GameClient.Instance);
		});
		Linha("Lugar", cli?.NomeDoLugar ?? "", null, c);

		Nota("Marcos são a moeda das habilidades (aba Learning). Recursos se tiram dos planetas (o verb Minerar) e pagam tecnologia no lugar do zeni, pelo dobro. A classe não aparece: ela se descobre jogando.", c);
	}

	/// <summary>Os tres textos vivos do cartao Estado. Uma conta so pra montagem e pra viva.</summary>
	private static string CadenciaDoSoco(SheetState f) => $"{f.SocoMs} ms";

	private static string ZeniDaStats(GameClient? cli) => $"{cli?.Zeni ?? 0:N0}";

	private static string RecursosDaStats(GameClient? cli) => $"{cli?.Recursos ?? 0:N0}";

	/// <summary>O nome do estilo de luta em uso, ou "nenhum". O id so sai se o catalogo nao o conhecer.</summary>
	private static string NomeDoEstilo(GameClient? cli)
	{
		if (cli == null || cli.EstiloAtual.Length == 0) return "nenhum";
		foreach (GameClient.EstiloInfo e in cli.Estilos)
			if (e.Id == cli.EstiloAtual) return e.Nome;
		return NomesLegiveis.Habilidade(cli.EstiloAtual);
	}

	/// <summary>
	/// A LINHA CUJO VALOR E UM ESTADO: rotulo apagado a esquerda e uma PILULA a direita, no lugar do
	/// texto -- "de pé", "NOCAUTEADO", "LETAL" se leem sem ler. O metadado `estado` leva o rotulo e a
	/// pilula leva o proprio texto (ver `Pilula`): e por eles que a bancada acha a linha. Nao leva o
	/// metadado `linha` de proposito -- `ValorDesenhado` espera um Label no lugar do valor, e aqui
	/// nao ha um.
	///
	/// DEVERIA SUBIR PRA `MenuJogo.Pecas.cs` no dia em que outra aba precisar dela (a Equip ja usa).
	/// </summary>
	private static HBoxContainer LinhaComPilula(string rotulo, string estado, Color cor, Control pai)
	{
		var h = new HBoxContainer();
		h.SetMeta("estado", rotulo);
		var a = new Label { Text = rotulo, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		a.AddThemeColorOverride("font_color", Tema.TextoFraco);
		a.AddThemeFontSizeOverride("font_size", 13);
		h.AddChild(a);
		PanelContainer p = Pilula(estado, cor);
		p.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		h.AddChild(p);
		pai.AddChild(h);
		return h;
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`): tudo que ESTA aba
	/// desenha e que a assinatura basica (em MenuJogo.cs) nao cobre, nos mesmos arredondamentos em
	/// que e desenhado. A basica ja tem a condicao e o golpe, quatro dos oito atributos, idade, classe e
	/// a secao de treino; entram aqui o bit do scouter (o ramo da faixa), os tanques, os outros quatro
	/// atributos, a vontade, a Sala, a raca, o estilo, os marcos e o lugar.
	///
	/// O QUE E VIVO NAO ENTRA (2026-10-19): o BP da faixa, os cinco vitais (com o trilho do Ki), a cadencia, o zeni
	/// e os recursos se reescrevem no lugar (`Viva`) -- assinados, eles remontavam a aba a cada ficha em luta.
	/// </summary>
	private string ExtraDaAssinaturaDeStats(SheetState f)
	{
		GameClient? c = GameClient.Instance;
		return $"{_atributos.Tem(Protocol.Poder.Scouter)}|{f.VigorMax:0}|{f.NutricaoMax:0}"
			 + $"|{_atributos.KiDef:0.##}|{_atributos.Technique:0.##}|{_atributos.KiSkill:0.##}|{_atributos.Esoteric:0.##}"
			 + $"|{_atributos.Willpower:0.##}|{_atributos.ZonaMult:0.##}|{_atributos.SalaFase}|{_atributos.SalaMinutos:0.#}"
			 + $"|{_atributos.Raca}|{c?.EstiloAtual}|{_livro.MarcosLivres}/{_livro.MarcosTotais}|{c?.NomeDoLugar}|{AssinaturaDoPoderEmUso()}";
	}
}
