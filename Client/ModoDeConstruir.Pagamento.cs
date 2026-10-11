using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// "PAGAR COM" -- o seletor do canto inferior direito do modo de construir: com o que as pecas sao pagas,
/// quanto custa a que esta na mao e quanto ha no bolso.
///
/// ============================ O PEDIDO ============================
/// O dono (2026-10-10): *"construcao de base: construir paredes etc tem custo em zenni/ recursos o usuario
/// escolhe na hora de construir e essa opcao fica selecionada ate ele quiser trocar (e uma opcao no canto
/// inferior direito)"*.
/// ==================================================================
///
/// ============================ COMO FICOU ============================
///   * DOIS BOTOES, "Zeni" e "Recursos": o aceso e a moeda em uso. Embaixo, o preco da peca que esta na mao
///     NAQUELA moeda (em recursos e o dobro) e o saldo dela -- vermelho quando nao paga a peca.
///   * A ESCOLHA FICA: vai pro `config.json` (`Settings.PagarObraCom`) na hora da troca, e por isso vale ao
///     sair e voltar pro modo e de uma sessao pra outra. Quem cobra e o servidor, que guarda a escolha por
///     jogador (`GameServer.CustoDeObra.cs`): o cliente a repete toda vez que LIGA o modo -- o servidor de
///     uma sessao nova so conhece o zeni de quem nunca escolheu.
///   * O FANTASMA FICA VERMELHO quando o bolso nao paga, com a frase que o servidor diria
///     (`PrecoDeBloco.FaltaSaldo`), e o clique nao manda nada. Arrastando, a pintura para na primeira peca
///     que nao da pra pagar.
///   * O CANTO: encostado na direita, com <see cref="LivreEmbaixo"/> px livres por baixo -- e ali que mora o
///     circulo do Dragon Radar de quem o tem.
/// ====================================================================
///
/// ============================ O SALDO DE AGORA, E NAO O DE DOIS QUADROS ATRAS ============================
/// O saldo que o seletor mostra e que o fantasma confere ja desconta as pecas A CAMINHO -- pedidas ao servidor
/// e ainda nao respondidas --, senao um arrasto pediria mais pecas do que o bolso paga. A conta e a do
/// <see cref="ContaDeObra"/> (Core): cada pedido de erguer entra nela com o preco, e cada resposta do servidor
/// (ele manda exatamente uma por pedido, tenha a peca subido ou nao) tira a mais antiga.
/// =========================================================================================================
/// </summary>
public partial class ModoDeConstruir
{
	/// <summary>DEFEITO INJETADO (bancada `--diagconstruir`): a troca de moeda nao vai pro `config.json` -- a escolha morre com a sessao. Falso em jogo, sempre.</summary>
	public static bool MoedaNaoGravaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--diagconstruir`): o fundo do seletor nao conta como interface -- o clique nele cai no mundo, na celula que esta por baixo. Falso em jogo, sempre.</summary>
	public static bool PagamentoNaoEInterfaceDeTeste;

	/// <summary>A margem da direita, em px de tela: a mesma do painel do corpo, no alto do HUD.</summary>
	public const float MargemDoPagamento = 14f;

	/// <summary>
	/// QUANTO FICA LIVRE POR BAIXO DO SELETOR, em px de tela. O canto inferior direito mesmo e do Dragon Radar
	/// (um circulo de ~170 px, pra quem tem o radar): o seletor mora logo acima, pra um nao cobrir o outro.
	/// </summary>
	public const float LivreEmbaixo = 190f;

	private PanelContainer _pagamento = null!;
	private readonly Button[] _botoesDeMoeda = new Button[2];   // pelo numero da `Moeda`
	private Label _preco = null!, _saldo = null!;

	/// <summary>A moeda escolhida. Nasce do `config.json` e so muda pelo seletor.</summary>
	private Moeda _moeda;

	/// <summary>A CONTA DO BOLSO, com as pecas a caminho: pedidos de erguer que o servidor ainda nao respondeu.</summary>
	private readonly ContaDeObra _conta = new();

	/// <summary>Ate que resposta de peca (`GameClient.RespostasDePecaDeObra`) a conta ja deu baixa.</summary>
	private int _respostasVistas;

	/// <summary>
	/// Quantos saldos tinham chegado quando o modo foi ligado. Ate chegar o proximo (a resposta do `obra_moeda`
	/// mandado ao ligar) o cliente NAO SABE quanto tem: o que ha guardado pode ser de outro personagem.
	/// </summary>
	private int _saldosAoLigar;

	/// <summary>
	/// O que o seletor esta mostrando: so se repinta quando isto muda (e conferido a cada quadro), ou quando
	/// alguem pede (<see cref="_repintar"/>: o modo ligou, a moeda trocou).
	/// </summary>
	private (Moeda Moeda, ClasseDeBloco? Classe, double Saldo, bool Sabe, bool Falta) _pintado;
	private bool _repintar = true;

	/// <summary>A frase de "falta saldo" do que esta pintado -- montada na pintura, e nao a cada quadro.</summary>
	private string _fraseSemSaldo = "";

	/// <summary>O seletor esta dizendo que o bolso nao paga a peca que esta na mao (o saldo em vermelho).</summary>
	private bool _faltaSaldo;

	/// <summary>
	/// DE QUANTO EM QUANTO TEMPO O CLIENTE RECONFERE O SALDO COM O SERVIDOR, enquanto o seletor diz que o bolso
	/// nao paga. O que ha guardado aqui e o que o servidor contou por ultimo; o bolso de verdade pode ter
	/// engordado por fora de uma obra (uma missao paga, um saque no banco) sem que ninguem avisasse este
	/// cliente -- e sem reconferir o fantasma ficaria vermelho, e o clique mudo, ate o jogador desligar e
	/// ligar o modo. Um pacote pequeno a cada dois segundos, e so pra quem esta olhando pra um "falta saldo".
	/// </summary>
	private const ulong ReconfereOSaldoACadaMs = 2000;
	private ulong _reconferiuEm;

	// =====================================================================
	// A MONTAGEM
	// =====================================================================
	/// <summary>A moeda que este arquivo de preferencias guarda. Texto que nao e moeda (ou arquivo antigo, sem o campo) = zeni.</summary>
	private void LerAEscolha(Settings config) => _moeda = PrecoDeBloco.DaChave(config.PagarObraCom) ?? Moeda.Zeni;

	private void MontarOPagamento()
	{
		LerAEscolha(Boot.Config);

		// ANCORADO NO CANTO E CRESCENDO PRA CIMA E PRA ESQUERDA (o molde do painel da direita do HUD): o painel
		// tem o tamanho do que cabe nele, e a borda de baixo nao sai do lugar se uma linha engordar.
		_pagamento = Tema.Painel1(10);
		_pagamento.AnchorLeft = _pagamento.AnchorRight = 1;
		_pagamento.AnchorTop = _pagamento.AnchorBottom = 1;
		_pagamento.OffsetLeft = _pagamento.OffsetRight = -MargemDoPagamento;
		_pagamento.OffsetTop = _pagamento.OffsetBottom = -LivreEmbaixo;
		_pagamento.GrowHorizontal = Control.GrowDirection.Begin;
		_pagamento.GrowVertical = Control.GrowDirection.Begin;
		_raiz.AddChild(_pagamento);

		var caixa = new VBoxContainer();
		caixa.AddThemeConstantOverride("separation", 4);
		_pagamento.AddChild(caixa);

		caixa.AddChild(Tema.Rotulo("pagar com"));

		var linha = new HBoxContainer();
		linha.AddThemeConstantOverride("separation", 4);
		caixa.AddChild(linha);
		foreach (Moeda m in new[] { Moeda.Zeni, Moeda.Recursos })
		{
			Moeda moeda = m;
			var b = new Button
			{
				Text = m == Moeda.Recursos ? "Recursos" : "Zeni",
				TooltipText = m == Moeda.Recursos
					? "pagar as peças com recursos (o dobro do preço em zeni)"
					: "pagar as peças com zeni",
				ToggleMode = true,
				CustomMinimumSize = new Vector2(104, 30),
				FocusMode = Control.FocusModeEnum.None,   // senao o Espaco (soco) "aperta" o botao com foco
			};
			b.Pressed += () => EscolherMoeda(moeda);
			linha.AddChild(b);
			_botoesDeMoeda[(int)m] = b;
		}

		_preco = new Label();
		_preco.AddThemeFontSizeOverride("font_size", 13);
		caixa.AddChild(_preco);

		_saldo = new Label();
		_saldo.AddThemeFontSizeOverride("font_size", 13);
		caixa.AddChild(_saldo);

		PintarOsBotoesDeMoeda();
	}

	/// <summary>O botao da moeda em uso fica afundado (o `pressed` do tema: borda e letra laranja); o outro, solto.</summary>
	private void PintarOsBotoesDeMoeda()
	{
		for (int i = 0; i < _botoesDeMoeda.Length; i++)
			_botoesDeMoeda[i].SetPressedNoSignal(i == (int)_moeda);
		_repintar = true;   // a moeda mudou (ou o modo ligou): o preco e o saldo sao outros
	}

	// =====================================================================
	// A ESCOLHA
	// =====================================================================
	/// <summary>
	/// O JOGADOR ESCOLHEU A MOEDA. Vai pro `config.json` na hora (e o que faz a escolha FICAR) e pro servidor,
	/// que e quem cobra: a partir da resposta dele, as pecas saem deste bolso.
	/// </summary>
	private void EscolherMoeda(Moeda moeda)
	{
		bool trocou = moeda != _moeda;
		_moeda = moeda;
		if (trocou && !MoedaNaoGravaDeTeste)
		{
			Boot.Config.PagarObraCom = PrecoDeBloco.Chave(moeda);
			Boot.Config.Gravar();
		}

		GameClient.Instance?.SendVerbo(PrecoDeBloco.Verbo, PrecoDeBloco.Chave(moeda));
		PintarOsBotoesDeMoeda();
		if (trocou)
			Chat.Sistema(moeda == Moeda.Recursos
				? "as peças agora saem dos seus recursos (o dobro do preço em zeni)."
				: "as peças agora saem do seu zeni.");
	}

	/// <summary>
	/// O MODO FOI LIGADO: o servidor fica sabendo (de novo) da moeda gravada, e responde com os dois saldos.
	/// Repetir a cada ligada e o que cobre a sessao nova, a troca de personagem e o servidor que reiniciou --
	/// e custa um pacote pequeno, so pra quem aperta a tecla.
	/// </summary>
	private void AoLigarOPagamento()
	{
		_conta.Limpar();
		if (GameClient.Instance is { } cli)
		{
			_respostasVistas = cli.RespostasDePecaDeObra;
			_saldosAoLigar = cli.SaldosDeObraRecebidos;
			cli.SendVerbo(PrecoDeBloco.Verbo, PrecoDeBloco.Chave(_moeda));
		}
		_faltaSaldo = false;
		_reconferiuEm = Time.GetTicksMsec();
		PintarOsBotoesDeMoeda();
	}

	// =====================================================================
	// O SALDO
	// =====================================================================
	/// <summary>O saldo que o servidor mandou e desta ligada do modo? Antes disso quem sabe e so ele.</summary>
	private bool SabeOSaldo(GameClient cli) => cli.SaldosDeObraRecebidos != _saldosAoLigar;

	/// <summary>
	/// O saldo nesta moeda, ja sem as pecas que estao a caminho do servidor. A carteira viva e a do
	/// `GameClient.Economia.cs` (zeni e recursos, como o servidor os contou por ultimo).
	/// </summary>
	private double SaldoDeAgora(GameClient cli, Moeda moeda) => _conta.Saldo(moeda, cli.Zeni, cli.Recursos);

	/// <summary>
	/// O BOLSO PAGA ESTA PECA, na moeda escolhida? Enquanto o saldo desta ligada nao chegou a resposta e "sim":
	/// e um palpite, como todo o resto do fantasma, e quem cobra e o servidor.
	/// </summary>
	private bool OBolsoPaga(BlocoDef def) =>
		GameClient.Instance is not { } cli || !SabeOSaldo(cli) || SaldoDeAgora(cli, _moeda) >= PrecoDeBloco.Na(def.Classe, _moeda);

	/// <summary>Um pedido de erguer acabou de sair: a peca entra na conta das que estao a caminho.</summary>
	private void AnotarPecaACaminho(GameClient cli, BlocoDef def) =>
		_conta.Pedir(_moeda, PrecoDeBloco.Na(def.Classe, _moeda), cli.Zeni, cli.Recursos);

	/// <summary>A frase de uma recusa. A de saldo leva os numeros -- e e a mesma que o servidor diria.</summary>
	private string FraseDaRecusa(RecusaDeBloco r) => r == RecusaDeBloco.SemSaldo ? _fraseSemSaldo : RegrasDeBloco.Motivo(r);

	/// <summary>
	/// A CADA QUADRO, com o modo ligado: da baixa nas pecas que o servidor respondeu e repinta o seletor se o
	/// que ele mostra mudou (a moeda, a classe da peca na mao, o saldo). Compara valores -- nao monta texto
	/// nenhum enquanto nada muda.
	/// </summary>
	private void AtualizarOPagamento(GameClient cli)
	{
		// UMA RESPOSTA POR PEDIDO, NA ORDEM: cada uma tira da conta a peca mais antiga, e a conta passa a partir
		// da carteira que veio nela (se chegaram varias de uma vez, a que fica e a da ultima -- que e a certa).
		while (_respostasVistas < cli.RespostasDePecaDeObra)
		{
			_respostasVistas++;
			_conta.Responder(cli.CarteiraNaUltimaObra.Zeni, cli.CarteiraNaUltimaObra.Recursos);
		}

		// O BOLSO PODE TER ENGORDADO POR FORA: enquanto o seletor diz "nao paga", o saldo e reconferido.
		if (_faltaSaldo && _conta.ACaminho == 0 && Time.GetTicksMsec() - _reconferiuEm >= ReconfereOSaldoACadaMs)
		{
			_reconferiuEm = Time.GetTicksMsec();
			cli.SendVerbo(PrecoDeBloco.Verbo, PrecoDeBloco.Chave(_moeda));
		}

		// O QUE SE MOSTRA: a moeda, a classe da peca na mao, o saldo (inteiro, pra baixo) e se ele paga a peca.
		// A pergunta "paga?" e feita com o saldo EXATO -- e a mesma do `OBolsoPaga`, que pinta o fantasma.
		BlocoDef? naMao = NaMao;
		bool sabe = SabeOSaldo(cli);
		double exato = sabe ? Math.Max(0, SaldoDeAgora(cli, _moeda)) : 0;
		bool falta = sabe && naMao != null && exato < PrecoDeBloco.Na(naMao.Classe, _moeda);
		(Moeda, ClasseDeBloco?, double, bool, bool) agora = (_moeda, naMao?.Classe, Math.Floor(exato + 1e-9), sabe, falta);
		if (!_repintar && _pintado == agora) return;
		_pintado = agora;
		_repintar = false;
		_faltaSaldo = falta;

		if (naMao != null)
		{
			string nome = PrecoDeBloco.NomeDaClasse(naMao.Classe);
			_preco.Text = $"{char.ToUpperInvariant(nome[0])}{nome[1..]}: {PrecoDeBloco.Etiqueta(naMao.Classe, _moeda)}";
			_fraseSemSaldo = PrecoDeBloco.FaltaSaldo(naMao.Classe, _moeda, exato);
		}
		else
		{
			_preco.Text = "casa vazia";
			_fraseSemSaldo = "";
		}

		_saldo.Text = sabe ? $"Você tem {PrecoDeBloco.Quantia(exato)} {PrecoDeBloco.Chave(_moeda)}" : "Você tem ...";
		_saldo.AddThemeColorOverride("font_color", falta ? Tema.Perigo : Tema.TextoFraco);
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagconstruir`)
	// =====================================================================
	/// <summary>A moeda escolhida no seletor.</summary>
	public Moeda MoedaDeTeste => _moeda;

	/// <summary>
	/// O QUE UMA SESSAO NOVA FAZ: le o `config.json` DO DISCO (e nao o que esta na memoria) e tira dele a moeda,
	/// pela mesma linha do <see cref="MontarOPagamento"/>. A bancada nao tem como fechar e abrir o jogo no meio.
	/// </summary>
	public void RenascerDoDiscoDeTeste()
	{
		LerAEscolha(Settings.Carregar());
		PintarOsBotoesDeMoeda();
	}

	/// <summary>O seletor esta na tela, e onde (em px de tela).</summary>
	public bool PagamentoNaTela => _ligado && IsInstanceValid(_pagamento) && _pagamento.IsVisibleInTree();
	public Rect2 PagamentoDeTeste => _pagamento.GetGlobalRect();

	/// <summary>O meio do botao de uma moeda, em px de tela -- pra bancada clicar nele com o mouse de verdade.</summary>
	public Vector2 BotaoDeMoedaDeTeste(Moeda moeda) => _botoesDeMoeda[(int)moeda].GetGlobalRect().GetCenter();

	/// <summary>Qual botao de moeda esta aceso (afundado).</summary>
	public bool BotaoDeMoedaAcesoDeTeste(Moeda moeda) => _botoesDeMoeda[(int)moeda].ButtonPressed;

	/// <summary>As duas linhas do seletor, como estao escritas, e se a do saldo esta em vermelho.</summary>
	public string PrecoDeTeste => _preco.Text;
	public string SaldoDeTeste => _saldo.Text;
	public bool SaldoEmVermelhoDeTeste => _saldo.GetThemeColor("font_color") == Tema.Perigo;

	/// <summary>Quantas pecas estao a caminho (pedidas e ainda nao respondidas).</summary>
	public int ACaminhoDeTeste => _conta.ACaminho;
}
