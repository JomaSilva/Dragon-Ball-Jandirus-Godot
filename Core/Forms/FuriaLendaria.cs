namespace Jandirus.Core.Forms;

/// <summary>
/// A FURIA LENDARIA -- porte de `Code/Modules/Skills/Buffs/racial/lssjbuff.dm:562-660`
/// (`legendary_berserk_check` / `legendary_berserk_loop` / `legendary_cur_mastery`).
///
/// ============================ ESTE SISTEMA JA EXISTIA NO DM, INTEIRO ============================
/// O bloco do original abre com o comentario que resume o pedido do dono palavra por palavra:
/// *"Legendary (normal ou Primal) que fica com raiva NUMA FORMA NAO MASTERIZADA perde o controle:
/// o corpo ataca TUDO que ve (player OU NPC) ... o jogador nao dirige o personagem nesse meio
/// tempo. **Receita do rampage do Oozaru selvagem** (`ctrlParalysis` trava a direcao; `MeleeAttack`
/// nao passa pelo gate de input)."*
///
/// Ou seja: o proprio DM diz que a fera e a furia sao O MESMO mecanismo. Por isso aqui nao ha uma
/// linha de IA, de bloqueio de input nem de replicacao -- tudo isso e o que
/// <see cref="Jandirus.Core.Ai.Cerebro"/> + `GameServer.Clone.SemAsRedeas` ja fazem pro macaco, e
/// o comentario daquela funcao ja anunciava este dia: *"a regra nao e do macaco: e de QUALQUER
/// corpo que o servidor esteja dirigindo. O proximo (uma possessao, um controle mental, um NPC que
/// toma um corpo) ja nasce obedecendo."*
///
/// O QUE E DESENHO NOVO E SO O RELOGIO -- ver <see cref="SegundosDeControle"/>.
/// ============================================================================================
///
/// ============================ QUAIS FORMAS, E A RESPOSTA E "A LINHA TODA" ============================
/// O gate do DM e `Class != "Legendary" &amp;&amp; Class != "Legendary Primal Saiyan"` mais
/// `if(!ssj &amp;&amp; !lssj) return` -- ou seja: **as duas classes lendarias, em QUALQUER forma
/// delas**, com `legendary_cur_mastery() &lt; 100`. Nao ha degrau de fora, nem o Wrathful (que la e
/// `lssj == 1` e le `lssj1mastery` como os outros).
///
/// Aqui isso vira uma pergunta pela LINHA, e nao pela classe, porque as duas dizem a mesma coisa e a
/// linha e o que este arquivo ja conhece: `Catalogo.LinhasAbertas` abre `Legendary` **so** pra quem
/// tem a classe Legendary e `LegendaryPrimal` **so** pro Primal (`Formas.cs:3391-3393`), e nenhuma
/// das duas classes tem a escada `Saiyajin` aberta. Perguntar pela classe seria carregar o mesmo
/// corte por outro caminho -- e um degrau lendario novo ja nasce dentro da regra.
/// ================================================================================================
/// </summary>
public static class FuriaLendaria
{
	/// <summary>
	/// ESTA FORMA PODE FUGIR DO CONTROLE? As duas linhas lendarias, sem excecao de degrau -- ver o
	/// cabecalho. Nao pergunta maestria: quem responde por "hoje" e
	/// <see cref="SegundosDeControle"/>, que devolve infinito pra forma dominada.
	///
	/// A SEPARACAO E DE PROPOSITO. Esta funcao e o que a COR DO OLHO consulta (`Catalogo.CorDoOlho`),
	/// e o cliente nao sabe a maestria de ninguem -- ele so recebe o bit de posse. Se ela perguntasse
	/// maestria, a pergunta nao teria resposta do outro lado do fio.
	/// </summary>
	public static bool EhDescontrolavel(FormaDef? d) =>
		d != null && d.Linha is LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal;

	/// <summary>
	/// A FORMA ESTA DOMINADA? `legendary_cur_mastery() >= 100` (`lssjbuff.dm:599`) -- o unico gate de
	/// maestria que o DM tem neste sistema, e ele e binario la: *"forma dominada = a mente segura a
	/// furia"*.
	/// </summary>
	public static bool Dominou(double maestria) => maestria >= 100;

	/// <summary>
	/// O PISO DO PRAZO, em segundos -- o mesmo do Oozaru, e BUSCADO em vez de repetido.
	///
	/// A razao escrita em <see cref="Oozaru.SegundosDeGraca"/> vale letra por letra aqui: sem piso, a
	/// primeira transformacao lendaria de um Saiyajin com 0% de maestria entregaria o corpo ao
	/// servidor no mesmo quadro em que a cena solta o boneco, e "perder o controle" deixaria de ser
	/// uma coisa que acontece pra ser o estado em que a forma nasce -- indistinguivel de defeito.
	///
	/// Repetir o 6 aqui seria a mesma verdade em dois lugares; no dia em que o dono achasse a janela
	/// curta, ele mudaria uma e nao a outra.
	/// </summary>
	public const double SegundosDeGraca = Oozaru.SegundosDeGraca;

	/// <summary>
	/// ============================ O RELOGIO DA LINHA, E ELE NAO E UM NUMERO NOVO ============================
	/// A curva precisa de uma DURACAO de referencia, e o Oozaru tinha a dele de graca (a forma cai
	/// sozinha em 300 s / 100 s). Uma forma da escada nao cai sozinha -- nao ha `Duracao` pra ler.
	///
	/// Em vez de inventar uma constante, isto le o relogio que a PROPRIA LINHA ja tem: os
	/// <see cref="Catalogo.RampaLssjSegundos"/> (180 s) e <see cref="Catalogo.RampaPrimalSegundos"/>
	/// (216 s) em que o combate continuo leva o multiplicador do minimo ao maximo
	/// (`LSSJ_RAMP_TICKS 600` e `combatTime / 720`). Tres razoes:
	///
	///   1. E o unico relogio que o DM da a esta linha, e ele mede exatamente a coisa certa -- quanto
	///      tempo a furia leva pra ferver por inteiro;
	///   2. as duas linhas ficam com prazos DIFERENTES sem nenhum `if`, e diferentes na direcao certa
	///      (o Primal, que e a linhagem, segura mais tempo);
	///   3. um numero a mais aqui seria um numero que ninguem consegue justificar depois -- e este ja
	///      esta justificado, com linha e arquivo, la em cima.
	/// ====================================================================================================
	/// </summary>
	public static double SegundosDaLinha(FormaDef? d) => d?.Linha switch
	{
		LinhaDeForma.Legendary => Catalogo.RampaLssjSegundos,
		LinhaDeForma.LegendaryPrimal => Catalogo.RampaPrimalSegundos,
		_ => 0,
	};

	/// <summary>
	/// ============================ QUANTO TEMPO VOCE DIRIGE ANTES DE PERDER ============================
	/// **A CURVA E DECISAO, E NAO PORTE.** No DM nao ha prazo nenhum: o berserk pega assim que a raiva
	/// pega e solta quando ela passa (`while(... Emotion == "Angry" ...)`), e a maestria e um
	/// interruptor -- abaixo de 100 voce SEMPRE perde, a partir de 100 voce NUNCA perde. Quem pediu a
	/// rampa foi o dono: *"o tempo q ela controla / o tempo ate perder o controle e baseado na
	/// maestria"*.
	///
	/// Entao esta e, literal, a MESMA curva do <see cref="Oozaru.SegundosDeControle"/> -- `D * f²`,
	/// com `f = maestria/100` e o piso de <see cref="SegundosDeGraca"/>. Copiar a forma da curva do
	/// macaco (em vez de inventar uma segunda) e o que faz as duas perdas de controle do jogo se
	/// sentirem como um sistema so: quem aprendeu a domar a fera ja sabe o que esperar da furia.
	/// O raciocinio de por que quadratica e nao linear esta escrito por inteiro la, e nao se repete.
	///
	/// AOS 100%, INFINITO -- e o `legendary_cur_mastery() >= 100` do DM, que e o unico ponto em que
	/// esta rampa e o original concordam exatamente.
	/// ============================================================================================
	/// </summary>
	public static double SegundosDeControle(FormaDef? d, double maestria)
	{
		if (!EhDescontrolavel(d)) return double.PositiveInfinity;
		if (Dominou(maestria)) return double.PositiveInfinity;

		double frac = Math.Clamp(maestria, 0, 100) / 100.0;
		return Math.Max(SegundosDeGraca, SegundosDaLinha(d) * frac * frac);
	}

	/// <summary>
	/// ============================ QUANTO TEMPO ELA FICA COM VOCE ============================
	/// **E A MESMA CURVA LIDA AO CONTRARIO**: `D * (1-f)²`. Nao ha segunda constante, nao ha segunda
	/// forma, nao ha segunda tabela -- `SegundosDePosse(m) == SegundosDeControle(100 - m)`, e e so
	/// isso que este metodo e.
	///
	/// O que essa simetria compra, e por isso ela foi escolhida:
	///
	///   * **as duas metades se cruzam nos 50%** -- exatamente meia maestria da 45 s dirigindo e 45 s
	///     possuido (no Legendary). O meio da barra vira um lugar que se RECONHECE em jogo, e nao um
	///     ponto qualquer de duas rampas independentes;
	///   * **o comeco e o fim sao espelhos**: aos 0% voce dirige o piso e ela fica com a forma inteira;
	///     aos 100% ela nao vem nunca. Uma segunda curva teria que ser calibrada pra dizer isso, e um
	///     dia alguem mexeria numa e nao na outra;
	///   * o dono pediu as duas coisas na mesma frase ("o tempo q ela controla / o tempo ate perder o
	///     controle"), e uma frase so devia virar um numero so.
	///
	/// O PISO VALE DOS DOIS LADOS e pelo motivo oposto: aos 99% de maestria a conta daria 0,018 s, e
	/// uma posse que comeca e termina no mesmo quadro nao e uma posse -- e um tremor na tela que
	/// ninguem consegue explicar. Aos 100% ela e ZERO de verdade, que e outra coisa: nao ha posse.
	/// ====================================================================================
	/// </summary>
	public static double SegundosDePosse(FormaDef? d, double maestria)
	{
		if (!EhDescontrolavel(d)) return 0;
		if (Dominou(maestria)) return 0;

		double frac = Math.Clamp(maestria, 0, 100) / 100.0;
		return Math.Max(SegundosDeGraca, SegundosDaLinha(d) * (1 - frac) * (1 - frac));
	}
}
