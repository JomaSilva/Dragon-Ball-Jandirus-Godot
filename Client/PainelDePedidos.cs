using Godot;
using Jandirus.Core.Verbos;

namespace Jandirus.Client;

/// <summary>
/// OS PEDIDOS QUE ESPERAM A MINHA RESPOSTA, no canto inferior direito -- um cartao por pedido, com o texto, quanto
/// falta do prazo e os dois botoes (dono, 2026-10-13: *"pedidos como pra ensinar técnica a aluno, convidar um player
/// pra ser aluno, etc deveriam aparecer na tela da pessoa como uma notificaçao (assim como tem de torneio) pra ela
/// aceitar"*).
///
/// E o irmao do <see cref="PainelDoTorneio"/>, empilhado logo ACIMA dele (o convite do torneio mora no mesmo canto):
/// o mais novo embaixo, perto de onde o olho ja esta.
///
/// ============================ O SERVIDOR E QUEM DECIDE ============================
/// A lista vem pronta no `S2C.Pedidos` (`GameServer.Pedidos.cs`) e este painel so a MOSTRA: os botoes mandam os
/// MESMOS verbs que o menu P ja tinha (`PedidosPendentes.Respostas` diz quais), e o servidor confere tudo de novo.
/// Respondido ou vencido, o pedido sai da lista do servidor e o cartao some no pacote seguinte; o relogio daqui so
/// escreve o numero -- quem faz o convite vencer e o servidor.
///
/// O CLIQUE APAGA O CARTAO NA HORA, sem esperar o pacote: a resposta leva ate um quinto de segundo pra voltar, e um
/// botao que continua ali depois do clique e um botao que se aperta duas vezes.
/// ==================================================================================
/// </summary>
public partial class PainelDePedidos : VBoxContainer
{
	/// <summary>A largura de um cartao, em px de tela -- a mesma do convite do torneio, que fica embaixo.</summary>
	private const float Largura = 300f;

	/// <summary>Quanto o painel sobe pra nao tapar o convite do torneio (112 px de altura, a 40 px do rodape).</summary>
	private const float AcimaDoTorneio = 40f + 112f + 8f;

	private sealed class Cartao
	{
		public Fatos Fato;
		public string Texto = "";
		public double Restante;
		public bool ComPrazo;
		public PanelContainer No = null!;
		public Label Prazo = null!;
	}

	private readonly List<Cartao> _cartoes = [];

	public override void _Ready()
	{
		AnchorLeft = 1; AnchorRight = 1; AnchorTop = 1; AnchorBottom = 1;
		OffsetLeft = -Largura - 14; OffsetRight = -14;
		OffsetTop = -AcimaDoTorneio; OffsetBottom = -AcimaDoTorneio;
		GrowHorizontal = GrowDirection.Begin;
		GrowVertical = GrowDirection.Begin;
		Alignment = AlignmentMode.End;
		AddThemeConstantOverride("separation", 6);
		MouseFilter = MouseFilterEnum.Ignore;
		SetProcess(false);

		if (GameClient.Instance is { } cli)
		{
			cli.PedidosMudaram += Refazer;
			Refazer();   // o pacote pode ter chegado antes de a tela existir
		}
	}

	/// <summary>Solta a assinatura -- o `GameClient` sobrevive ao logout (ver a nota no `MenuJogo._Ready`).</summary>
	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.PedidosMudaram -= Refazer;
	}

	/// <summary>A lista do servidor mudou: os cartoes sao refeitos do zero (sao um, dois, raramente tres).</summary>
	private void Refazer()
	{
		foreach (Cartao c in _cartoes) c.No.QueueFree();
		_cartoes.Clear();

		foreach (GameClient.PedidoInfo p in GameClient.Instance?.Pedidos ?? [])
			_cartoes.Add(Montar(p));

		SetProcess(_cartoes.Exists(c => c.ComPrazo));
	}

	private Cartao Montar(GameClient.PedidoInfo p)
	{
		var cartao = new Cartao { Fato = p.Fato, Texto = p.Texto, Restante = p.Segundos, ComPrazo = p.Segundos > 0 };

		cartao.No = new PanelContainer { CustomMinimumSize = new Vector2(Largura, 0) };
		Tema.Aplicar(cartao.No);
		AddChild(cartao.No);

		var v = new VBoxContainer();
		v.AddThemeConstantOverride("separation", 6);
		cartao.No.AddChild(v);

		Label texto = Tema.Legenda(p.Texto, Tema.Destaque, 14);
		texto.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		texto.CustomMinimumSize = new Vector2(Largura - 24, 0);
		v.AddChild(texto);

		cartao.Prazo = Tema.Legenda("", Tema.TextoFraco, 12);
		cartao.Prazo.Visible = cartao.ComPrazo;
		v.AddChild(cartao.Prazo);
		EscreverOPrazo(cartao);

		var linha = new HBoxContainer { Alignment = AlignmentMode.End };
		linha.AddThemeConstantOverride("separation", 8);
		v.AddChild(linha);

		(string sim, string nao) = PedidosPendentes.Respostas(p.Fato);
		(string rotuloSim, string rotuloNao) = PedidosPendentes.Rotulos(p.Fato);
		linha.AddChild(Botao(rotuloSim, sim, cartao));
		linha.AddChild(Botao(rotuloNao, nao, cartao));
		return cartao;
	}

	private Button Botao(string rotulo, string chave, Cartao cartao)
	{
		var b = new Button { Text = rotulo };
		b.SetMeta("chave", chave);
		b.Pressed += () =>
		{
			Mandar(chave);
			// O CARTAO SAI NA HORA -- ver o cabecalho. O pacote seguinte confirma (ou o devolve, se o servidor recusou).
			_cartoes.Remove(cartao);
			cartao.No.QueueFree();
		};
		return b;
	}

	/// <summary>
	/// Manda um verb pela CHAVE do registro (`RequisitosDosVerbos`): o prefixo diz o canal -- a habilidade
	/// (`hab:mst_aceitar`) ou o verbo com argumento (`verbo:potencial_aceitar`).
	/// </summary>
	private static void Mandar(string chave)
	{
		if (GameClient.Instance is not { } cli || chave.Length == 0) return;

		if (chave.StartsWith(RequisitosDosVerbos.PrefixoDeHabilidade, StringComparison.Ordinal))
		{
			cli.SendHabilidade(chave[RequisitosDosVerbos.PrefixoDeHabilidade.Length..]);
			return;
		}
		if (!chave.StartsWith(RequisitosDosVerbos.PrefixoDeVerbo, StringComparison.Ordinal)) return;

		string resto = chave[RequisitosDosVerbos.PrefixoDeVerbo.Length..];
		int espaco = resto.IndexOf(' ');
		if (espaco < 0) cli.SendVerbo(resto);
		else cli.SendVerbo(resto[..espaco], resto[(espaco + 1)..]);
	}

	public override void _Process(double delta)
	{
		foreach (Cartao c in _cartoes)
		{
			if (!c.ComPrazo) continue;
			c.Restante = Math.Max(c.Restante - delta, 0);
			EscreverOPrazo(c);
		}
	}

	private static void EscreverOPrazo(Cartao c)
	{
		if (c.ComPrazo) c.Prazo.Text = $"responda em {Math.Ceiling(c.Restante):0} s";
	}

	// =====================================================================
	// AS PORTAS DE BANCADA (`--diagv021`)
	// =====================================================================
	/// <summary>Os cartoes na tela agora, de cima pra baixo: (o texto, os rotulos dos botoes, os segundos que o cartao mostra).</summary>
	public List<(string Texto, List<string> Botoes, double Segundos)> CartoesDeTeste()
	{
		var saida = new List<(string, List<string>, double)>();
		foreach (Cartao c in _cartoes)
		{
			if (!IsInstanceValid(c.No) || c.No.IsQueuedForDeletion()) continue;
			var botoes = new List<string>();
			foreach (Node n in c.No.FindChildren("*", nameof(Button), recursive: true, owned: false))
				if (n is Button b) botoes.Add(b.Text);
			saida.Add((c.Texto, botoes, c.ComPrazo ? c.Restante : 0));
		}
		return saida;
	}

	/// <summary>Aperta o botao com este rotulo no cartao cujo texto contem o trecho -- o mesmo sinal do dedo.</summary>
	public bool ApertarDeTeste(string trechoDoTexto, string rotulo)
	{
		foreach (Cartao c in _cartoes)
		{
			if (!IsInstanceValid(c.No) || !c.Texto.Contains(trechoDoTexto, StringComparison.Ordinal)) continue;
			foreach (Node n in c.No.FindChildren("*", nameof(Button), recursive: true, owned: false))
			{
				if (n is not Button b || b.Text != rotulo) continue;
				b.EmitSignal(BaseButton.SignalName.Pressed);
				return true;
			}
		}
		return false;
	}
}
