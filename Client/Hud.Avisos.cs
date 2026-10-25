using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// **OS AVISOS DO CORPO, NO PAINEL DE STATUS** -- as linhas que dizem ao dono do corpo um estado que o SPRITE nao
/// mostra: a guarda erguida, o atordoamento, o agarrao.
///
/// ============================ POR QUE ELAS PRECISARAM EXISTIR ============================
/// O dono (2026-10-12): *"adicione algum aviso no canto da tela ao apertar o botao de guarda pra avisar q vc ta com
/// a guarda levantada, ja q n tem diferença de sprite e a galera tava confusa, assim como um aviso apenas pro
/// jogador local q ele ta stunado/atordoado"*. A guarda nao tem pose propria na folha do BYOND, e o atordoamento
/// e um relogio do servidor: de fora os dois sao um corpo parado, e por dentro eram uma tecla que "nao funcionava".
///
/// O agarrao entrou junto pelo mesmo motivo: quem e pego precisa ler que apertar as direcoes e a saida, e ver a
/// luta andar.
///
/// QUEM DIZ E A FICHA (`SheetState`), e nao a tecla: a guarda que o servidor RECUSA (sem braco nem perna pra
/// aparar) nao acende o aviso, e a que ele ergue acende mesmo se o pedido veio de outro lugar.
///
/// SO O DONO LE: a ficha e um pacote pessoal. Ninguem em volta fica sabendo que o outro esta atordoado.
/// ========================================================================================
/// </summary>
public partial class Hud
{
	private VBoxContainer _avisosDoCorpo = null!;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagv020`): o painel nao escreve aviso nenhum -- o HUD de antes de 2026-10-12.
	/// Lido pelo <see cref="MostrarOsAvisosDoCorpo"/>.
	/// </summary>
	public static bool AvisosDoCorpoMudosDeTeste;

	/// <summary>As linhas como sairam na tela, na ordem. Pra bancada, e so pra ela.</summary>
	public List<string> AvisosDoCorpoDeTeste =>
		[.. _avisosDoCorpo.GetChildren().OfType<Label>().Where(l => l.Visible).Select(l => l.Text)];

	/// <summary>Reescreve os avisos agora, com esta ficha (a bancada nao espera o pacote de 5 Hz).</summary>
	public void RedesenharOsAvisosDoCorpoDeTeste(SheetState f) => MostrarOsAvisosDoCorpo(f);

	/// <summary>A coluna nasce vazia, no pe do painel de status: sem aviso o painel e o de sempre.</summary>
	private void MontarOsAvisosDoCorpo(VBoxContainer coluna)
	{
		_avisosDoCorpo = new VBoxContainer { Name = "AvisosDoCorpo", Visible = false };
		_avisosDoCorpo.AddThemeConstantOverride("separation", 2);
		coluna.AddChild(_avisosDoCorpo);
	}

	private void MostrarOsAvisosDoCorpo(SheetState f)
	{
		// O PESO VEM DA FICHA LENTA (o mesmo `weight` que o servidor usa no custo do treino): ele sobe o fundo do "EXAUSTO".
		List<(string Texto, Color Cor)> avisos = AvisosDoCorpoMudosDeTeste ? [] : AvisosDoCorpo(f, GameClient.Instance?.Atributos.PesoMult ?? 1);

		// AS LINHAS SAO REAPROVEITADAS: a ficha chega cinco vezes por segundo, e criar e soltar um Label a cada uma
		// seria lixo pro coletor por um texto que quase nunca muda.
		List<Label> linhas = [.. _avisosDoCorpo.GetChildren().OfType<Label>()];
		for (int i = linhas.Count; i < avisos.Count; i++)
		{
			var l = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
			l.AddThemeFontSizeOverride("font_size", 14);
			_avisosDoCorpo.AddChild(l);
			linhas.Add(l);
		}
		for (int i = 0; i < linhas.Count; i++)
		{
			bool vale = i < avisos.Count;
			linhas[i].Visible = vale;
			if (!vale) continue;
			if (linhas[i].Text != avisos[i].Texto) linhas[i].Text = avisos[i].Texto;
			linhas[i].AddThemeColorOverride("font_color", avisos[i].Cor);
		}
		_avisosDoCorpo.Visible = avisos.Count > 0;
	}

	// =====================================================================
	// AS FRASES -- funcao pura da ficha
	// =====================================================================
	public const string AvisoDeGuarda = "GUARDA LEVANTADA";
	public const string AvisoDeAtordoado = "ATORDOADO";
	public const string AvisoDeExausto = "EXAUSTO · sem vigor pra treinar · descanse";
	public const string AvisoDeSemComida = "SEM COMIDA · o vigor não volta · coma alguma coisa";

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagvigortreino`): o painel volta a calar o vigor e a comida -- o HUD de antes de 2026-10-24, em que o
	/// treino parava e o poder "congelava" sem a tela dizer por que. Lido pelas duas linhas do <see cref="AvisosDoCorpo"/>. Falso em jogo.
	/// </summary>
	public static bool AvisosDoVigorMudosDeTeste;

	/// <summary>
	/// O QUE O PAINEL DIZ DESTE CORPO AGORA, de cima pra baixo. Caido (o nocaute, o cadaver) nao ha aviso: o corpo no chao ja e
	/// a leitura, e "atordoado" por cima de um nocaute seria ruido.
	///
	/// O MORTO DE PE TEM AVISO (dono, 2026-10-16): *"n aparece a informaçao de guarda levantada quando vc defende quando morto"*. O
	/// corte era o `Imobilizado` cru (`KO || Morto`), e o morto de pe -- no Outro Mundo, de visita, de passagem -- anda, luta e ergue a
	/// guarda desde 2026-09-04. A regra e a do corpo local (`LocalPlayer.Caido`): o nocaute e o cadaver.
	/// </summary>
	/// <param name="pesoDoTreino">O peso do corpo (o `PesoMult` da ficha lenta): ele encarece o passe do treino, e com ele o fundo do
	/// "EXAUSTO". Sem ficha lenta, 1.</param>
	public static List<(string Texto, Color Cor)> AvisosDoCorpo(SheetState f, double pesoDoTreino = 1)
	{
		List<(string, Color)> l = [];
		if (LocalPlayer.Caido(f)) return l;

		if (f.Agarrado)
		{
			string comeco = f.RaboPreso ? "RABO PRESO" : f.Estrangulado ? "ESTRANGULADO" : "AGARRADO";
			l.Add(($"{comeco} · aperte as teclas de andar pra se soltar ({f.Escape}%)", Tema.Perigo));
		}
		if (f.Atordoado) l.Add((AvisoDeAtordoado, Tema.Perigo));
		if (f.Guarda) l.Add((AvisoDeGuarda, Tema.Ki));
		if (f.Estrangulando) l.Add(("ESTRANGULANDO · parado enquanto aperta · o soco afrouxa", Tema.Destaque));
		else if (f.Segurando) l.Add(("SEGURANDO ALGUÉM · ande pra arremessar · o soco estrangula", Tema.Destaque));
		// (v0.4.0) AS PASSIVAS E ATIVAS RACIAIS -- ja vem escritas do servidor, uma por linha (`SheetState.Raciais`).
		if (!string.IsNullOrEmpty(f.Raciais))
			foreach (string linha in f.Raciais.Split('\n', StringSplitOptions.RemoveEmptyEntries)) l.Add((linha, Tema.Ki));

		// ============================ O VIGOR E A COMIDA (dono, 2026-10-24: "Avisar e mostrar no HUD") ============================
		// O treino parava no fim do vigor e o poder "congelava" sem a tela dizer por que -- o relato dos jogadores. Duas linhas, no pe da
		// coluna (embaixo das barras de vida, Ki, vigor e nutricao, que e onde o olho ja procura o folego), e so quando valem:
		//   * EXAUSTO: o vigor nao paga nem um passe de treino -- a MESMA conta que recusa o T no servidor (`Cansaco.AguentaTreinar`);
		//   * SEM COMIDA: o estomago vazio, que nao devolve vigor nenhum (`Nutricao.Digerir` sai na entrada) -- so com o vigor fora do cheio:
		//     quem o tem travado no cheio (o Majin, o androide de energia infinita, o morto) nao precisa de comida pra ele.
		// So com a ficha ja chegada (`VigorMax`/`NutricaoMax` acima de zero). O EXAUSTO no vermelho do perigo (ele trava o T); o SEM
		// COMIDA na cor da barra de nutricao, logo acima. (Desenho meu, minimo: duas linhas de texto, sem icone nem barra nova.)
		// =========================================================================================================================
		if (!AvisosDoVigorMudosDeTeste && f.VigorMax > 0)
		{
			if (!Jandirus.Core.Stats.Cansaco.AguentaTreinar(f.Vigor, f.VigorMax, pesoDoTreino, Jandirus.Core.Stats.Cansaco.SegundosDoPasseDoTreino))
				l.Add((AvisoDeExausto, Tema.Perigo));
			if (f.NutricaoMax > 0 && f.Nutricao <= 0 && f.Vigor < f.VigorMax) l.Add((AvisoDeSemComida, Tema.Nutricao));
		}
		return l;
	}
}
