using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ O RESCALDO VISTO POR DOIS CLIENTES DE VERDADE (`--destrocos &lt;a|b&gt;`) ============================
/// O dono escreveu, com parenteses e tudo: *"ele vai sumir do espaco pra todos os jogadores (server
/// sync)"*. **"Pra todos" e uma afirmacao sobre PROCESSOS, e nao sobre codigo** -- e ela nao tem como
/// ser verdadeira nem falsa num processo so.
///
/// A `--diagagonia` mede o rescaldo inteiro no pixel, mas ela tem UM cliente e nenhuma rede: o que ela
/// chama de "duas telas" sao dois `DestrocosNoEspaco` na mesma memoria, com a mesma DLL, o mesmo
/// `static` e a mesma lista de mortos escrita a mao pela propria bancada. Tres coisas que so existem
/// com dois processos ficam de fora dela, e as tres sao justamente o que o dono grifou:
///
///   1. **a lista de mortos VIAJA** (`S2C.Mortos` -> `AplicarMortos`), em vez de ser injetada;
///   2. **o sumico e do SERVIDOR**: o planeta tem que sair da tela dos DOIS, e cada um decide isso
///      sozinho, com a propria copia do registro. Um cliente que ignorasse o pacote continuaria vendo
///      o disco -- e e exatamente esse o defeito que esta bancada existe pra pegar;
///   3. **o determinismo e entre MAQUINAS**: as pedras nascem de `(semente, indice, tempo)` sem um
///      byte no fio, entao "todo mundo ve a mesma pedra" so vale se dois processos, com duas memorias
///      e dois relogios, chegarem no MESMO numero.
///
/// ============================ O SERVIDOR DIRIGE, OS CLIENTES RELATAM, O SERVIDOR JULGA ============================
/// Mesma divisao da `--menteviva` e da `--vozviva`. O que este no faz e o que so o dono da tela pode
/// fazer: olhar a arvore de cena DELE e escrever o que viu. Quem compara os dois relatos e o servidor
/// (`GameServer.DestrocosVivosTeste.cs`), porque o veredito de uma bancada de sincronia nao pode morar
/// dentro de um dos dois lados que ele julga.
///
/// ============================ O CANAL DE VOLTA E UM ARQUIVO, E ISSO E DE PROPOSITO ============================
/// O relato vai pro `user://`, no mesmo formato do `RoboDeMorteVista` -- que ja resolveu este problema
/// aqui. A alternativa seria um opcode novo no protocolo **so pra bancada**, e este projeto tem regra
/// contra isso: o fio carrega o jogo, nao a medicao. Os dois processos moram na mesma maquina por
/// construcao (uma bancada de rede local), entao o `user://` e o mesmo dos dois lados.
///
/// **E O RELATO CARREGA O TOKEN DA RODADA.** Um arquivo deixado por uma rodada anterior descreveria um
/// planeta que morreu meia hora atras, e o veredito leria sincronia onde nao houve nada -- que e
/// literalmente um defeito ja acontecido nesta casa (ver o `LerOMorto` do `RoboDeMorteVista`).
/// ============================================================================================================
///
/// COMO RODAR -- `testar-destrocos.bat`. Ou na mao:
///     Godot --headless --path . --host --rede 7983 --destrocosvivos --destrocos a
///           --raca Saiyan --conta bancada_destrocos_a --nome DestrocoA
///     Godot --headless --path . --rede 7983 --connect 127.0.0.1 --destrocos b
///           --raca Saiyan --conta bancada_destrocos_b --nome DestrocoB
/// </summary>
public partial class RoboDeDestrocos : Node
{
	/// <summary>`a` ou `b`. Os dois fazem a MESMA coisa -- a bancada e sobre eles concordarem.</summary>
	public string Papel = "a";

	/// <summary>O arquivo que este papel escreve, no `user://` compartilhado pelos dois processos.</summary>
	public static string Arquivo(string papel) => $"user://destrocos-relato-{papel}.txt";

	private GameClient? _cli;
	private string _token = "", _fase = "", _planeta = "";

	/// <summary>
	/// Quantos segundos o relato espera depois do anuncio.
	///
	/// Nao e folga pra rede (o anuncio ja chegou): e folga pra o CLIENTE terminar de reagir ao que
	/// chegou junto. `AplicarMortos` dispara `MortosMudaram`, o `DesenharPlanetas` remonta a lista
	/// inteira de orbes e os `QueueFree` so tem efeito no fim do quadro. Relatar no mesmo quadro do
	/// anuncio leria uma arvore no meio da troca.
	/// </summary>
	private const double EsperaAntesDeRelatar = 0.6;

	private double _relatarEm = -1;

	public override void _Ready()
	{
		if (GameClient.Instance is not { } cli) return;
		_cli = cli;
		// METODO NOMEADO E `-=` NO `_ExitTree`, nunca lambda -- este projeto ja pagou 19 assinaturas
		// orfas por ciclo de relog por causa de lambda que nao da pra cancelar.
		cli.Falou += AoOuvirOAnuncio;
		GD.Print($"[destrocos:{Papel}] no ar, esperando o anuncio do servidor.");
	}

	public override void _ExitTree()
	{
		if (_cli is { } cli) cli.Falou -= AoOuvirOAnuncio;
	}

	/// <summary>
	/// O ANUNCIO DE FASE, PELO CANAL DE TEXTO -- a mesma sincronia da `--vozviva`, e pelo mesmo motivo:
	/// se cada cliente contasse o proprio relogio, um relataria a fase seguinte enquanto o servidor
	/// ainda nao tinha destruido nada, e a divergencia apareceria como "o planeta nao sumiu".
	/// </summary>
	private void AoOuvirOAnuncio(Protocol.Fala canal, string autor, string texto)
	{
		if (canal != Protocol.Fala.Sistema
			|| !texto.StartsWith("[destrocos] fase", StringComparison.Ordinal)) return;

		foreach (string parte in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] kv = parte.Split('=', 2);
			if (kv.Length != 2) continue;
			switch (kv[0])
			{
				case "fase": _fase = kv[1]; break;
				case "token": _token = kv[1]; break;
				case "planeta": _planeta = kv[1]; break;
			}
		}

		GD.Print($"[destrocos:{Papel}] --> fase '{_fase}' (planeta {_planeta}, token {_token})");
		_relatarEm = EsperaAntesDeRelatar;
	}

	public override void _Process(double delta)
	{
		if (_relatarEm <= 0) return;
		_relatarEm -= delta;
		if (_relatarEm <= 0) Relatar();
	}

	/// <summary>
	/// ============================ O QUE ESTE PROCESSO VE, ESCRITO ============================
	/// Tudo daqui sai da ARVORE DE CENA deste cliente -- nao ha uma unica pergunta ao servidor. E essa
	/// e a ideia: o servidor ja sabe o que ele mandou; o que ninguem sabe e o que apareceu na tela de
	/// cada um.
	///
	/// **AS POSICOES SAO RELATIVAS AO CAMPO, E TAMBEM ABSOLUTAS.** As duas, porque elas reprovam por
	/// motivos diferentes: a relativa e a saida crua da funcao pura (um `Random` local a faz divergir),
	/// e a absoluta pega o campo montado no lugar errado -- que daria pedras identicas em orbitas
	/// diferentes.
	/// =====================================================================================
	/// </summary>
	private void Relatar()
	{
		_relatarEm = -1;

		var sb = new System.Text.StringBuilder();
		sb.Append("token=").Append(_token).Append('\n');
		sb.Append("fase=").Append(_fase).Append('\n');
		sb.Append("papel=").Append(Papel).Append('\n');

		// O DISCO DO PLANETA. `PlanetaDesenhado` e o node que o `DesenharPlanetas` cria pra cada corpo
		// vivo da vizinhanca -- e o ramo do MORTO e justamente o que nao o cria. Entao a presenca
		// deste node E a resposta pra "o planeta sumiu?".
		PlanetaDesenhado? disco = null;
		DestrocosNoEspaco? campo = null;

		if (World.Instancia?.GetNodeOrNull<Node2D>("Planetas") is { } orbes)
			foreach (Node n in orbes.GetChildren())
			{
				if (n.IsQueuedForDeletion()) continue;
				if (n is PlanetaDesenhado p && p.Nome == _planeta) disco = p;
				if (n is DestrocosNoEspaco d && d.Chave.Texto == _planeta) campo = d;
			}

		sb.Append("planeta=").Append(disco != null ? '1' : '0').Append('\n');
		sb.Append("campo=").Append(campo != null ? '1' : '0').Append('\n');

		double? prazo = _cli?.SegundosAteOEstouro(new ChaveDePlaneta(true, _planeta, 0));
		sb.Append("prazo=").Append(prazo is { } q
			? q.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "nulo").Append('\n');

		if (campo != null)
		{
			Vector2 raiz = campo.GlobalPosition;
			sb.Append("cacos=").Append(campo.CacosDeTeste).Append('\n');
			sb.Append("raiz=").Append(Numero(raiz.X)).Append(',').Append(Numero(raiz.Y)).Append('\n');
			sb.Append("pos=").Append(Posicoes(campo, raiz)).Append('\n');

			// ============================ E AGORA O MESMO INSTANTE NOS DOIS PROCESSOS ============================
			// **A LINHA `pos=` ACIMA NAO SERVE PRA COMPARAR OS DOIS, E A PRIMEIRA RODADA PROVOU**: ela
			// saiu com 144,53 num cliente e 146,87 no outro, e a bancada leu isso como divergencia. Nao
			// era. Os dois relatos foram escritos com 0,3 s de diferenca (latencia do anuncio + o
			// proprio `_Process` de cada um), e o `prazo` de cada um dizia isso com todas as letras:
			// -6,8 contra -7,1. **Duas fotos de instantes diferentes de uma coisa que se move.**
			//
			// O pedido e *"dois clientes, MESMO INSTANTE, mesmas pedras nas mesmas posicoes"*, e o
			// mesmo instante e o que esta linha produz: os dois pedem ao campo DELES onde as pedras
			// estariam em <see cref="InstanteCanonico"/>, pela mesma porta que o jogo usa a cada
			// quadro (`AplicarTempo`). O `_Process` devolve o campo pro relogio de verdade no quadro
			// seguinte, sozinho -- porque ele le o prazo do `GameClient`, e nao um estado proprio.
			// ==================================================================================================
			campo.AplicarTempo(InstanteCanonico);
			sb.Append("instante=").Append(InstanteCanonico
				.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
			sb.Append("posfixo=").Append(Posicoes(campo, raiz)).Append('\n');
		}
		else sb.Append("cacos=0\npos=\nposfixo=\n");

		using (Godot.FileAccess? f = Godot.FileAccess.Open(Arquivo(Papel), Godot.FileAccess.ModeFlags.Write))
			f?.StoreString(sb.ToString());

		GD.Print($"[destrocos:{Papel}] relatei a fase '{_fase}': planeta={(disco != null ? 1 : 0)} "
			   + $"campo={(campo != null ? 1 : 0)} cacos={campo?.CacosDeTeste ?? 0} prazo={prazo:0.0}");
	}

	/// <summary>
	/// O INSTANTE EM QUE OS DOIS SE PERGUNTAM A MESMA COISA -- 10 s depois do estouro.
	///
	/// Dez porque tem que estar **dentro da janela** (60 s, senao o campo se recolhe) e **depois da
	/// entrada** (a opacidade so abre em 2,2 s), com folga dos dois lados. Qualquer valor nessa faixa
	/// serviria: o que importa e ele ser o MESMO nos dois processos, e ele e uma constante compilada.
	/// </summary>
	private const double InstanteCanonico = 10.0;

	/// <summary>As posicoes dos cacos, relativas a raiz do campo -- a saida crua da funcao pura.</summary>
	private static string Posicoes(DestrocosNoEspaco campo, Vector2 raiz)
	{
		var sb = new System.Text.StringBuilder();
		foreach (Vector2 p in campo.OndeDeTeste)
		{
			if (sb.Length > 0) sb.Append(';');
			sb.Append(Numero(p.X - raiz.X)).Append(',').Append(Numero(p.Y - raiz.Y));
		}
		return sb.ToString();
	}

	/// <summary>
	/// Duas casas e ponto decimal INVARIANTE.
	///
	/// A cultura da maquina nao entra aqui: com virgula decimal o `pos=` viraria `12,34,56,78` e a
	/// comparacao entre os dois relatos passaria a depender do idioma do Windows -- que e o tipo de
	/// divergencia que uma bancada de determinismo nao pode ter dentro dela mesma.
	/// </summary>
	private static string Numero(float v) =>
		v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
