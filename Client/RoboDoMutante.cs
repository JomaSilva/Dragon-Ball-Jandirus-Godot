using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DO FROST DEMON MUTANTE (`--diagmutante`, 2026-10-19). O dono: *"frost demon mutante deveria ter como escolher todas as
/// transformaçoes na criaçao de personagem assim como era nesse projeto E:\Users\Joao\Desktop\Desktop\Finale-master [...] e ta todo bugado
/// e sem as cinematicas q deveriam ter"* -- e *"apertar C duas vezes como frost ele deve ir pro proxima transformaçao e na primeira vez com
/// a cinematica, e apertar X segue a logica das outras transformaçoes"*.
///
/// SEM `--host`: o robo sobe o servidor deste processo e entra pelas TELAS DE PRODUCAO do `Boot` -- o login, a selecao e a criacao,
/// apertadas com o mesmo `Pressed` do dedo do jogador -- e nasce pelo `CreateChar` de verdade; dentro do mundo, as teclas C e X sao as do
/// teclado. O palco (`GameServer.MutantePalco.cs`) so faz o que o acaso e o relogio fariam: poe o Mutante na vaga, ensina a reunir
/// energia e queima o fusivel na hora.
///
///   M1. a vaga chega com a classe de Frost Demon na lista de slots, e a do Mutante abre AS SUAS FORMAS com SETE abas e o aviso de quem
///       nasceu Mutante (foto `mutante-1-criacao-sete-formas`); CONTRA-EXEMPLO (`CreationScreen.ClasseDoFrostIgnoradaDeTeste`): a mesma
///       vaga abre com tres (foto `mutante-1b-defeito-tres-formas`); e reaberta com o Frost Demon ja escolhido, volta as sete; a
///       descricao da raca diz que a classe ja esta sorteada, e nao que o combate a revela; CONTRA-EXEMPLO
///       (`CreationScreen.ClasseDoFrostNoCombateDeTeste`);
///   M2. ele escolhe o corpo das tres primeiras cascas e nasce Mutante, na 1a Forma, com os sete corpos que escolheu -- a lista casa com
///       a marca da lista torta, e o login nao a "desentorta" porque a criacao grava a versao das listas;
///   M3. o painel mostra a maestria da base; CONTRA-EXEMPLO (`PainelDoMutanteMudoDeTeste`);
///   M4. o C duplo abre a 2a Forma com a cena GRANDE VERMELHA -- a chama, os aneis, a aura colada no corpo (que sai na virada) e o
///       banho vermelho da virada (foto
///       `mutante-2-cena-vermelha`); a 3a, com o defeito `CenaSemOVermelhoDeTeste`, sai sem o vermelho (foto
///       `mutante-2b-defeito-cena-sem-vermelho`); cada X recua UMA casca; o segundo C na 2a e o SURTO vermelho (foto
///       `mutante-3-surto-vermelho`); com o defeito `DescontroleSemCenaDeTeste`, o surto de sempre;
///   M5. o ki trava: a aura vermelha cola no corpo, o `chargeaura` toca e o painel diz KI DESCONTROLADO (foto
///       `mutante-4-ki-descontrolado`); segurar C nao acende a carga; recuar devolve o controle e a aura sai; CONTRA-EXEMPLO
///       (`AuraDoDescontroleMudaDeTeste`);
///   M7. o nocaute mantem a forma (o dono: *"nocaute deveria manter a forma atual"*): o Mutante cai na 2a Forma e continua nela, deitado
///       com o corpo da forma (foto `mutante-6-nocauteado-na-forma`); CONTRA-EXEMPLO (`NocauteDesfazOFrostDeTeste`) -- roda antes do M6;
///   M6. o `Icer Form Settings` da aba Other abre a tela das formas com as sete (foto `mutante-5-icer-form-settings`), e o Salvar troca o
///       corpo da forma base e devolve o corpo a 1a Forma.
///
/// `--defeitoversao`: a rodada do contra-exemplo da criacao (`NasceSemAVersaoDosCorposDeTeste`) -- o Mutante nasce sem a versao das
/// listas, o login entorta a lista legitima dele, e a regua de M2 reprova. So M1 e M2.
///
///     Godot --path . --diagmutante --resolution 1280x720 --rede 8861 --campoteste 8 --horateste 0.5 --semfauna
/// Sem janela nao ha foto, mas as medidas saem.
/// </summary>
public partial class RoboDoMutante : Node
{
	private const string Conta = "bancada_dmutante";
	private const string Senha = "dmutante";
	private const string Prefixo = "[dmutante] ";

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>
	/// OS CORPOS DAS TRES PRIMEIRAS CASCAS QUE O ROBO ESCOLHE NA CRIACAO -- o resto fica no padrao. A lista casa com a marca da lista torta
	/// (`FormasDeFrost.ListaTortaDoMutante`) de proposito: e o Mutante novo que a versao gravada pela criacao protege.
	/// </summary>
	private static readonly string[] Escolhidos = [FormasDeFrost.Catalogo[20], FormasDeFrost.Catalogo[21], FormasDeFrost.Catalogo[22]];

	/// <summary>O corpo novo da forma base, trocado pelo `Icer Form Settings`.</summary>
	private static readonly string NovoDaBase = FormasDeFrost.Catalogo[30];

	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _fotos = [];
	private readonly List<string> _ditas = [];
	private readonly List<string> _sons = [];
	private List<SlotInfo> _slots = [];
	private int _listas;
	private bool _soAVersao;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready()
	{
		_soAVersao = Array.IndexOf(OS.GetCmdlineArgs(), "--defeitoversao") >= 0;
		// O SERVIDOR SOBE AQUI, como na `--diagfilho`: sem `--host` ninguem o levanta, e o robo entra pelo login como o jogador entra.
		if (S is { } srv && !srv.Running && !srv.Start(Porta()))
		{
			GD.PushError(Prefixo + "nao consegui abrir a porta -- ha outra bancada viva?");
			return;
		}
		if (C is not { } cli) return;
		cli.SlotsRecebidos += AoReceberSlots;
		cli.Falou += AoOuvir;
		AudioDirector.Espiao += AoSoar;
		_ = Rodar();
	}

	public override void _ExitTree()
	{
		if (C is { } cli)
		{
			cli.SlotsRecebidos -= AoReceberSlots;
			cli.Falou -= AoOuvir;
		}
		AudioDirector.Espiao -= AoSoar;
	}

	private static int Porta()
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, "--rede");
		return i >= 0 && i + 1 < a.Length && int.TryParse(a[i + 1], out int p) && p > 0 ? p : Protocol.DefaultPort;
	}

	private void AoReceberSlots(List<SlotInfo> slots)
	{
		_slots = slots;
		_listas++;
	}

	private void AoOuvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private void AoSoar(string caminho, float volume) => _sons.Add(caminho);

	private bool Disse(string trecho) => _ditas.Exists(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private bool Soou(string trecho) => _sons.Exists(s => s.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private string Ultimas() => string.Join(" | ", _ditas.TakeLast(3));

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e)
		{
			Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})");
			GD.Print(e.ToString());
		}
		finally
		{
			CreationScreen.ClasseDoFrostIgnoradaDeTeste = false;
			CreationScreen.ClasseDoFrostNoCombateDeTeste = false;
			Transformacao.CenaSemOVermelhoDeTeste = false;
			Cinematicas.DescontroleSemCenaDeTeste = false;
			Jandirus.Server.GameServer.PalcoM_Limpar();
		}
		Fechar();
	}

	private async Tarefa Percurso()
	{
		GD.Print(Prefixo + "===== O FROST DEMON MUTANTE (dono, 2026-10-19) =====");
		if (C is not { } cli || S is not { } srv) { Conferir(false, "o cliente e o servidor deste processo existem"); return; }
		await Segundos(1.0);

		// ---------------------------------------------------------- M1. a vaga e a criacao
		cli.Conectar("127.0.0.1", Porta(), Conta, Senha);
		if (!await Ate(() => _listas > 0, 40)) { Conferir(false, "a conta abriu e a lista de vagas chegou"); return; }
		string sorteada = _slots.Count > 0 ? _slots[0].ClasseDoFrost ?? "" : "";
		Conferir(sorteada is FormasDeFrost.ClasseNormal or FormasDeFrost.ClasseMutante,
				 $"M1. a vaga vazia chega com a classe de Frost Demon que o servidor sorteou pra ela ('{sorteada}') -- a criacao sabe quantos "
				 + "corpos pedir");
		int listas = _listas;
		bool vagaDoMutante = srv.PalcoM_VagaMutante(Conta, 0) && await Ate(() => _listas > listas, 10);
		Conferir(vagaDoMutante && _slots[0].ClasseDoFrost == FormasDeFrost.ClasseMutante,
				 "(palco) a vaga 1 sai MUTANTE -- o 1% que o robo nao pode esperar sair -- e a lista de vagas volta com ela");

		CharacterSelect? selecao = await Achar<CharacterSelect>(GetTree().Root, s => s.Visible, 10);
		CreationScreen? criacao = await AbrirACriacao(selecao);
		if (selecao == null || criacao == null) { Conferir(false, "a selecao de producao abre a criacao no 'Criar personagem'"); return; }
		EscolhaDeFormasDoFrost esc = criacao.EscolhaDeFormasDeTeste;
		bool naEtapa = await IrAte(criacao, "AS SUAS FORMAS");
		await Segundos(0.6);
		Fotografar("mutante-1-criacao-sete-formas");
		// AS AFIRMACOES DA TELA VALEM QUANDO A VAGA CHEGOU MUTANTE PELO FIO: sem a classe no fio quem reprova sao as duas de cima, e
		// cada falha fica com um dono so -- a contraprova por mutacao precisa saber QUAL linha caiu. O mesmo vale pro resto do roteiro:
		// a afirmacao de uma propriedade (a cor da cena, os corpos do nascimento, o corpo novo do Salvar) so vale quando o fato de que ela
		// fala aconteceu, e o fato tem a afirmacao propria dele.
		bool vagaMutante = _slots.Count > 0 && _slots[0].ClasseDoFrost == FormasDeFrost.ClasseMutante;
		Conferir(!vagaMutante || (naEtapa && esc.AbasDeTeste == FormasDeFrost.Total && esc.AvisoDeTeste.Contains("MUTANTE")),
				 "M1. a vaga do Mutante abre AS SUAS FORMAS com SETE abas -- as quatro cascas, a base e as duas evolucoes -- e o aviso de quem "
				 + $"nasceu Mutante [etapa '{criacao.TituloDaEtapaDeTeste}', {esc.AbasDeTeste} aba(s), aviso '{Curto(esc.AvisoDeTeste)}']");

		// CONTRA-EXEMPLO: a criacao que ignora a classe da vaga -- pelo mesmo caminho (a selecao reabre a criacao na vaga do Mutante).
		await VoltarPraSelecao(criacao);
		CreationScreen.ClasseDoFrostIgnoradaDeTeste = true;
		try
		{
			await AbrirACriacao(selecao);
			await IrAte(criacao, "AS SUAS FORMAS");
			await Segundos(0.4);
			Fotografar("mutante-1b-defeito-tres-formas");
			Conferir(esc.AbasDeTeste != FormasDeFrost.Total,
					 "M1. CONTRA-EXEMPLO (defeito injetado: a criacao ignora a classe da vaga, o jogo de antes): a mesma vaga do Mutante abre com "
					 + $"{esc.AbasDeTeste} abas -- a regua REPROVA");
		}
		finally { CreationScreen.ClasseDoFrostIgnoradaDeTeste = false; }

		// A REABERTURA REFAZ A LISTA: o Frost Demon ja esta escolhido (a tela nao e destruida), entao desta vez o robo so avanca -- sem
		// clicar no mundo nem na raca, que refariam a lista por conta propria. Quem tem de trocar os tres corpos de ha pouco pelos sete e
		// o `Reabrir` da vaga do Mutante.
		await VoltarPraSelecao(criacao);
		await AbrirACriacao(selecao);
		bool reaberta = await IrAte(criacao, "AS SUAS FORMAS", escolher: false);
		Conferir(!vagaMutante || (reaberta && esc.AbasDeTeste == FormasDeFrost.Total),
				 "M1. reaberta na vaga do Mutante com o Frost Demon ja escolhido, a criacao volta as SETE abas -- a lista se refaz na reabertura "
				 + $"[{esc.AbasDeTeste} aba(s)]");

		// A DESCRICAO DA RACA NAO DESMENTE O AVISO: a classe do Frost Demon ja vem sorteada com a vaga, e a frase de sempre das racas de
		// linhagem sorteada ("a sua e revelada quando o combate mostrar") contradizia o "VOCE NASCEU UM FROST DEMON MUTANTE" logo acima dela.
		string descricao = criacao.DescricaoDeTeste;
		Conferir(reaberta && !descricao.Contains("revelada quando o combate") && descricao.Contains("a etapa das formas avisa"),
				 $"M1. a descricao da raca diz que a classe do Frost Demon ja esta sorteada -- e nao que o combate a revela ['{descricao}']");
		CreationScreen.ClasseDoFrostNoCombateDeTeste = true;
		try
		{
			Apertar(Botao(criacao, "Voltar"));
			await Segundos(0.2);
			await IrAte(criacao, "AS SUAS FORMAS", escolher: false);
			Conferir(criacao.DescricaoDeTeste.Contains("revelada quando o combate"),
					 "M1. CONTRA-EXEMPLO (defeito injetado: a frase de antes, a do combate): a mesma etapa volta a desmentir o aviso -- a regua REPROVA");
		}
		finally { CreationScreen.ClasseDoFrostNoCombateDeTeste = false; }

		// ---------------------------------------------------------- M2. as tres primeiras cascas, e o nascimento
		for (int slot = 0; slot < Escolhidos.Length; slot++)
		{
			esc.AbrirAbaDeTeste(slot);
			await Quadros(2);
			esc.EscolherDeTeste(Escolhidos[slot]);
			await Quadros(2);
		}
		bool identidade = await IrAte(criacao, "QUEM É VOCÊ?");
		if (Todos<LineEdit>(criacao).FirstOrDefault(l => l.PlaceholderText == "nome do personagem") is { } nome) nome.Text = "Gelado";
		if (Todos<TextEdit>(criacao).FirstOrDefault() is { } historia)
		{
			historia.Text = "Um Frost Demon Mutante, nascido de uma bancada de robo.";
			historia.EmitSignal(TextEdit.SignalName.TextChanged);   // o `Text` escrito por codigo nao avisa
		}
		await Segundos(0.3);
		int abasNaCriacao = esc.AbasDeTeste;   // sete: a tela mandou os sete corpos escolhidos (ver o M2)
		if (_soAVersao) Jandirus.Server.GameServer.NasceSemAVersaoDosCorposDeTeste = true;
		Apertar(Botao(criacao, "Entrar no mundo"));
		bool nasceu = identidade && await Ate(() => C is { LocalId: > 0 } && World.Instancia != null && Estado() != null, 40);
		List<string> escolhidos = [.. Escolhidos, .. Enumerable.Range(4, 4).Select(FormasDeFrost.PadraoDoDegrau)];
		var nascido = Estado();
		string ficha = nascido is { } n0 ? $"{n0.Classe}, {n0.Forma}: {string.Join(", ", n0.Corpos)}" : "sem corpo";
		if (_soAVersao)
		{
			Conferir(nasceu && nascido is { } n1 && !n1.Corpos.SequenceEqual(escolhidos),
					 "M2. CONTRA-EXEMPLO (defeito injetado: o personagem novo nasce sem a versao das listas de corpos): o login 'desentorta' a lista "
					 + $"legitima do Mutante -- a regua de M2 REPROVA [{ficha}]");
			return;
		}
		Conferir(nasceu && nascido is { } n2 && n2.Classe == FormasDeFrost.ClasseMutante && n2.Forma == "frost1"
				 && (abasNaCriacao != FormasDeFrost.Total || n2.Corpos.SequenceEqual(escolhidos)),
				 $"M2. ele NASCE Mutante, na 1a Forma -- e com os SETE corpos que escolheu nas sete abas: o login nao 'desentorta' a lista dele [{ficha}]");
		if (!nasceu) return;

		int eu = Eu;
		await Segundos(1.5);
		srv.EmbateDeFoto_CeuLimpo(eu);
		await AteACenaAcabar();

		// ---------------------------------------------------------- M3. o painel
		bool comPainel = await Ate(() => Painel().Contains("MAESTRIA DA BASE"), 8);
		Conferir(comPainel, $"M3. o painel do Mutante mostra a maestria da base embaixo das linhas raciais [{Curto(Painel())}]");
		Jandirus.Server.GameServer.PainelDoMutanteMudoDeTeste = true;
		try
		{
			bool sumiu = await Ate(() => !Painel().Contains("MAESTRIA DA BASE"), 8);
			Conferir(sumiu, "M3. CONTRA-EXEMPLO (defeito injetado: o painel do Mutante some, o jogo de antes): a linha sai da ficha -- a regua REPROVA");
		}
		finally { Jandirus.Server.GameServer.PainelDoMutanteMudoDeTeste = false; }
		await Ate(() => Painel().Contains("MAESTRIA DA BASE"), 8);

		// ---------------------------------------------------------- M4. o C duplo e o X, pelo teclado
		// A CAMADA DA CENA NO BONECO, FORA DA CENA: o mesmo `VestirColadaDaCena` que a `Transformacao` chama, direto. Separa a afirmacao de
		// que o boneco DESENHA o que a cena pede (as linhas do `CharacterVisual`) da de que a cena PEDE (a linha da `Transformacao`, o M4d).
		VisualDe(eu)?.VestirColadaDaCena(FormasDeFrost.ColadaDoDescontrole);
		await Quadros(2);
		bool desenhou = ColadaVermelha();
		VisualDe(eu)?.VestirColadaDaCena(null);
		await Quadros(2);
		Conferir(desenhou && !ColadaVermelha(), $"M4m. o boneco desenha a camada que a cena pede -- e a tira quando ela sai [coladas: {Coladas()}]");

		_ditas.Clear();
		string subiu = await CDuplo();
		Transformacao? grande = await CenaQueComecou();
		await Segundos(3.0);
		bool coladaNaCena = ColadaVermelha();
		Fotografar("mutante-2-cena-vermelha");
		// UMA AFIRMACAO POR PEDACO DA CENA, e nao uma so com tudo: cada linha do conserto guarda um pedaco diferente (a escolha da cena,
		// a cor dos aneis, a da chama, a aura colada, o aviso), e a contraprova por mutacao precisa saber QUAL caiu.
		string daCena = $"[{subiu}; {Cena(grande)}; coladas: {Coladas()}; ouvi: {Ultimas()}]";
		bool aGrande = grande?.CenaDeTeste == Cinematicas.FrostDescontrole;
		Conferir(subiu == "frost2", $"M4. o C duplo na 1a Forma abre a 2a Forma [{subiu}]");
		Conferir(aGrande, $"M4a. ...e, a 0% de maestria, a PRIMEIRA vez e a cena GRANDE do descontrole {daCena}");
		Conferir(!aGrande || Vermelho(grande!.CorDosAneisDeTeste), $"M4b. ...e a cena do descontrole tem os aneis de choque VERMELHOS {daCena}");
		Conferir(!aGrande || Vermelho(grande!.CorDaChamaDaCenaDeTeste), $"M4c. ...e a chama grande VERMELHA {daCena}");
		Conferir(!aGrande || !desenhou || coladaNaCena, $"M4d. ...e a cena veste a aura ameacadora vermelha no corpo durante ela {daCena}");
		Conferir(Disse("demais para segurar"), $"M4e. ...e o aviso de que aquele poder e demais pra segurar {daCena}");
		// NA VIRADA, E NAO NO FIM: a aura da cena sai do corpo quando a forma assume (o `removeOverlay` do DM), com a cena ainda no ar.
		double virada = Cinematicas.FrostDescontrole.SegundosAteAVirada;
		bool passouDaVirada = await Ate(() => grande is not { } g || g.FimDeTeste != Transformacao.FimDaCena.Rodando || g.TempoDeTeste > virada + 0.2, 35);
		bool aindaNoAr = grande?.FimDeTeste == Transformacao.FimDaCena.Rodando;
		bool saiuNaVirada = !ColadaVermelha();
		// E O BANHO DA VIRADA E VERMELHO: o corpo se lava na cor da cena (o `animate(color)` do DM), lido no material do corpo.
		var banho = VisualDe(eu)?.LavagemDeTeste;
		await AteACenaAcabar();
		Conferir(!aGrande || (passouDaVirada && aindaNoAr && saiuNaVirada && grande?.FimDeTeste == Transformacao.FimDaCena.Sozinha && !ColadaVermelha()),
				 "M4f. na VIRADA da cena do descontrole a aura dela sai do corpo, com a cena ainda no ar -- e a cena acaba sozinha (o ki ainda "
				 + $"esta na mao) [virada aos {virada:0.#} s; no ar depois dela {aindaNoAr}; fim {grande?.FimDeTeste}; coladas: {Coladas()}]");
		Conferir(!aGrande || (banho is { } b1 && b1.Mistura > 0.05f && Vermelho(b1.Cor)),
				 $"M4j. ...e na virada o corpo se lava de VERMELHO [banho {Banho(banho)}]");

		Transformacao.CenaSemOVermelhoDeTeste = true;
		try
		{
			string terceira = await CDuplo();
			Transformacao? semVermelho = await CenaQueComecou();
			await Segundos(3.0);
			bool coladaNoDefeito = ColadaVermelha();
			Fotografar("mutante-2b-defeito-cena-sem-vermelho");
			Conferir(terceira == "frost3" && semVermelho is { } sv && sv.CenaDeTeste == Cinematicas.FrostDescontrole
					 && !(Vermelho(sv.CorDosAneisDeTeste) && Vermelho(sv.CorDaChamaDaCenaDeTeste) && coladaNoDefeito),
					 "M4. CONTRA-EXEMPLO (defeito injetado: a cena do descontrole na cor da forma e sem a aura colada, o jogo de antes): a 3a Forma "
					 + $"abre a cena grande sem o vermelho -- a regua REPROVA [{terceira}; {Cena(semVermelho)}; coladas: {Coladas()}]");
			await AteACenaAcabar();
		}
		finally { Transformacao.CenaSemOVermelhoDeTeste = false; }

		string desceu = await TeclaX() + " > " + await TeclaX();
		Conferir(desceu == "frost2 > frost1", $"M4g. cada X recua UMA casca: da 3a pra 2a, da 2a pra 1a [{desceu}]");

		string deNovo = await CDuplo();
		Transformacao? surto = await CenaQueComecou();
		await Quadros(3);   // o beat zero roda no primeiro quadro da cena
		var banhoDoSurto = VisualDe(eu)?.LavagemDeTeste;   // o surto lava o corpo na largada (o beat zero)
		await Segundos(0.5);
		Fotografar("mutante-3-surto-vermelho");
		bool oSurto = surto?.CenaDeTeste == Cinematicas.FrostSurtoDoDescontrole;
		Conferir(deNovo == "frost2" && oSurto,
				 $"M4h. o segundo C duplo pra 2a Forma e o SURTO do descontrole (a versao curta), e nao a estreia de novo [{deNovo}; {Cena(surto)}]");
		Conferir(!oSurto || Vermelho(surto!.CorDosAneisDeTeste), $"M4i. ...e o surto e VERMELHO [{Cena(surto)}]");
		Conferir(!oSurto || (banhoDoSurto is { } b2 && b2.Mistura > 0.05f && Vermelho(b2.Cor)),
				 $"M4k. ...e lava o corpo de VERMELHO [banho {Banho(banhoDoSurto)}]");
		await AteACenaAcabar();

		await TeclaX();
		Cinematicas.DescontroleSemCenaDeTeste = true;
		try
		{
			string comDefeito = await CDuplo();
			Transformacao? deSempre = await CenaQueComecou();
			Conferir(comDefeito == "frost2" && deSempre?.CenaDeTeste != Cinematicas.FrostSurtoDoDescontrole,
					 "M4. CONTRA-EXEMPLO (defeito injetado: o bit do descontrole ignorado, o jogo de antes): a 2a Forma abre com o surto de sempre "
					 + $"dela -- a regua REPROVA [{comDefeito}; {Cena(deSempre)}]");
			await AteACenaAcabar();
		}
		finally { Cinematicas.DescontroleSemCenaDeTeste = false; }

		// ---------------------------------------------------------- M5. o ki trava
		srv.PalcoM_SabeReunir(eu);
		_sons.Clear();
		bool armou = srv.PalcoM_QueimarOFusivel(eu);
		await Ate(() => Estado()?.Travado == true && cli.FrostDescontrolado(eu), 6);
		await Segundos(0.8);
		bool vermelha = ColadaVermelha();
		Fotografar("mutante-4-ki-descontrolado");
		string sons = string.Join(", ", _sons.TakeLast(4).Select(s => s.Split('/')[^1]));
		bool noFio = cli.FrostDescontrolado(eu), virou = cli.FrostDescontroleVirou(eu);
		Conferir(armou && Estado()?.Travado == true && noFio,
				 $"M5. O KI TRAVA na forma que ele nao segura: o pacote da trava chega e diz que o ki esta descontrolado [travado {Estado()?.Travado}]");
		Conferir(virou, "M5v. ...e traz a VIRADA (o instante em que o ki travou, e nao o estado de quem chega na zona)");
		Conferir(!noFio || (VisualDe(eu)?.DescontroladoDeTeste == true && vermelha),
				 $"M5x. ...e o boneco veste a aura ameacadora VERMELHA [coladas: {Coladas()}]");
		Conferir(!(noFio && virou) || Soou("chargeaura"), $"M5a. ...e o `chargeaura` toca no instante da trava [sons: {sons}]");
		bool painelTravado = await Ate(() => Painel().Contains("KI DESCONTROLADO"), 8);
		Conferir(painelTravado, $"M5b. ...e o painel diz KI DESCONTROLADO, com a liberacao [{Curto(Painel())}]");
		Tecla(Key.C, true);
		await Segundos(0.7);
		bool reuniu = Estado()?.Carregando == true;
		Tecla(Key.C, false);
		await Segundos(0.3);
		Conferir(!reuniu, "M5c. ...e segurar C com o ki travado NAO acende a carga (e ele sabe reunir energia: o Ki Unlocked do palco)");
		srv.PalcoM_SabeReunir(eu, false);   // o C duplo de daqui pra frente nao pode encher o Ki acima de 100% (ver o palco)
		string recuou = await TeclaX();
		bool apagou = await Ate(() => Estado()?.Travado == false && VisualDe(eu)?.DescontroladoDeTeste == false && !ColadaVermelha(), 12);
		Conferir(recuou == "frost1" && apagou,
				 $"M5d. recuar pra casca que ele segura devolve o controle -- e a aura vermelha sai do corpo [{recuou}; coladas: {Coladas()}]");

		string outraVez = await CDuplo();
		await AteACenaAcabar();
		_sons.Clear();
		Jandirus.Server.GameServer.AuraDoDescontroleMudaDeTeste = true;
		try
		{
			bool armouDeNovo = srv.PalcoM_QueimarOFusivel(eu);
			bool travouCalado = await Ate(() => Estado()?.Travado == true, 6);
			await Segundos(1.2);
			Conferir(outraVez == "frost2" && armouDeNovo && travouCalado && VisualDe(eu)?.DescontroladoDeTeste != true && !ColadaVermelha()
					 && !Soou("chargeaura"),
					 "M5. CONTRA-EXEMPLO (defeito injetado: o descontrole nao vai pro fio, o jogo de antes): o ki trava e o corpo continua sem a "
					 + $"aura vermelha, e calado -- a regua REPROVA [{outraVez}; coladas: {Coladas()}]");
		}
		finally { Jandirus.Server.GameServer.AuraDoDescontroleMudaDeTeste = false; }
		await TeclaX();
		await Ate(() => Estado()?.Travado == false && VisualDe(eu)?.DescontroladoDeTeste == false, 12);

		// ---------------------------------------------------------- M7. o nocaute mantem a forma
		// O dono: *"nocaute deveria manter a forma atual"*. Pelo nocaute de producao (`CombatState.Nocautear`, o do palco), na 2a Forma:
		// a forma fica no servidor e o boneco cai DEITADO com o corpo da forma (a pose `ko` da folha dela -- o corpo de baixo esta
		// escondido em toda forma do Frost). Antes do M6 de proposito: o M6 sai mais cedo quando a tela dele nao abre.
		string antesDoNocaute = await CDuplo();
		await AteACenaAcabar();
		string folhaDaForma = VisualDe(eu)?.FolhaDoCorpoDaFormaDeTeste ?? "";
		srv.PalcoM_Nocautear(eu, 30);
		bool deitou = await Ate(() => VisualDe(eu)?.AnimacaoDaFormaDeTeste.StartsWith("ko") == true, 5);
		await Segundos(0.6);
		Fotografar("mutante-6-nocauteado-na-forma");
		string formaCaido = Forma();
		string folhaCaido = VisualDe(eu)?.FolhaDoCorpoDaFormaDeTeste ?? "";
		string poseCaido = VisualDe(eu)?.AnimacaoDaFormaDeTeste ?? "";
		srv.PalcoM_Levantar(eu);
		await Segundos(0.8);
		Conferir(antesDoNocaute == "frost2" && formaCaido == "frost2" && deitou && folhaCaido == folhaDaForma,
				 "M7. o NOCAUTE mantem a forma: o Mutante cai na 2a Forma e continua nela, deitado com o corpo da forma "
				 + $"[antes {antesDoNocaute}, caido {formaCaido}; pose {poseCaido}; folha {folhaCaido.Split('/')[^1]}]");
		Jandirus.Server.GameServer.NocauteDesfazOFrostDeTeste = true;
		try
		{
			srv.PalcoM_Nocautear(eu, 30);
			await Segundos(1.0);
			string desfez = Forma();
			srv.PalcoM_Levantar(eu);
			await Segundos(0.8);
			await AteACenaAcabar();
			Conferir(desfez != "frost2",
					 $"M7. CONTRA-EXEMPLO (defeito injetado: o nocaute desfaz a escada, o jogo de antes): o Mutante acorda na {desfez} -- a regua REPROVA");
		}
		finally { Jandirus.Server.GameServer.NocauteDesfazOFrostDeTeste = false; }
		if (Forma() != "frost1") await TeclaX();   // o M6 parte da 1a Forma

		// ---------------------------------------------------------- M6. o Icer Form Settings
		string pra2 = await CDuplo();
		await AteACenaAcabar();
		_ditas.Clear();
		Verbo? verbo = Verbos.PorChave("Icer Form Settings");
		verbo?.Acionar?.Invoke();
		TelaDeFormasDoFrost? tela = TelaDeFormasDoFrost.Instancia;
		bool abriu = await Ate(() => tela?.AbertaDeTeste == true, 4);
		await Segundos(0.5);
		Fotografar("mutante-5-icer-form-settings");
		Conferir(verbo != null && abriu && tela?.EscolhaDeTeste.AbasDeTeste == FormasDeFrost.Total
				 && tela.EscolhaDeTeste.AvisoDeTeste == EscolhaDeFormasDoFrost.AvisoEmJogo,
				 "M6. o `Icer Form Settings` da aba Other abre a tela das formas com as SETE do Mutante -- o mesmo componente da criacao "
				 + $"[verb {(verbo != null ? "visivel" : "AUSENTE")}, aberta {abriu}, {tela?.EscolhaDeTeste.AbasDeTeste} aba(s)]");
		if (!abriu || tela == null) return;
		tela.EscolhaDeTeste.AbrirAbaDeTeste(4);
		await Quadros(2);
		tela.EscolhaDeTeste.EscolherDeTeste(NovoDaBase);
		await Quadros(2);
		tela.SalvarDeTeste();
		bool respondeu = await Ate(() => Disse("trocados"), 8);
		bool trocou = await Ate(() => World.Instancia?.MeuLook is { } l && l.Ap.FormasDeFrost.Count == FormasDeFrost.Total
									  && l.Ap.FormasDeFrost[4] == NovoDaBase, 4);
		await AteACenaAcabar();
		Conferir(respondeu && !tela.AbertaDeTeste, $"M6b. o Salvar fecha a tela e o servidor responde que trocou os corpos [ouvi: {Ultimas()}]");
		Conferir(!respondeu || trocou,
				 "M6c. ...e o corpo novo da forma base chega no boneco (o mundo ve os corpos novos) "
				 + $"[{(World.Instancia?.MeuLook is { } l2 ? string.Join(", ", l2.Ap.FormasDeFrost) : "sem ficha")}]");
		Conferir(!respondeu || (pra2 == "frost2" && Forma() == "frost1"),
				 $"M6d. ...e o corpo volta a 1a Forma (o `formchoose` do DM) [estava em {pra2}, agora {Forma()}]");
	}

	// =====================================================================
	// AS TELAS DE PRODUCAO
	// =====================================================================
	/// <summary>O "Criar personagem" da selecao de producao, e a criacao que ele abre.</summary>
	private async System.Threading.Tasks.Task<CreationScreen?> AbrirACriacao(CharacterSelect? selecao)
	{
		if (selecao == null) return null;
		await Ate(() => selecao.Visible, 5);
		Apertar(Botao(selecao, "Criar personagem"));
		return await Achar<CreationScreen>(GetTree().Root, c => c.Visible, 8);
	}

	/// <summary>
	/// O "Voltar" da criacao ate a primeira etapa -- e dela, de volta pra selecao (o `Cancelado`). Na primeira etapa o mesmo botao
	/// se chama "Cancelar" (`CreationScreen`: `PrimeiraEtapa ? "Cancelar" : "Voltar"`).
	/// </summary>
	private async Tarefa VoltarPraSelecao(CreationScreen criacao)
	{
		for (int i = 0; i < 16 && criacao.Visible; i++)
		{
			Apertar(Botao(criacao, "Voltar") ?? Botao(criacao, "Cancelar"));
			await Segundos(0.2);
		}
	}

	/// <summary>
	/// Anda a criacao ate a etapa deste titulo -- escolhendo Vegeta e o Frost Demon quando passa por eles (com <paramref name="escolher"/>;
	/// sem ele, so o "Avançar", com o que a tela ja tinha escolhido).
	/// </summary>
	private async System.Threading.Tasks.Task<bool> IrAte(CreationScreen criacao, string titulo, bool escolher = true)
	{
		for (int i = 0; i < 16 && criacao.TituloDaEtapaDeTeste != titulo; i++)
		{
			if (escolher && criacao.TituloDaEtapaDeTeste == "DE ONDE VEM O SEU POVO?") Apertar(Botao(criacao, "Vegeta"));
			if (escolher && criacao.TituloDaEtapaDeTeste == "ESCOLHA SUA RAÇA") Apertar(Botao(criacao, NomeDaRaca.Bonito(FormasDeFrost.Raca)));
			await Quadros(2);
			Apertar(Botao(criacao, "Avançar"));
			await Segundos(0.25);
		}
		return criacao.TituloDaEtapaDeTeste == titulo;
	}

	// =====================================================================
	// AS TECLAS
	// =====================================================================
	private static void Tecla(Key k, bool apertada) =>
		Input.ParseInputEvent(new InputEventKey { Keycode = k, PhysicalKeycode = k, Pressed = apertada });

	/// <summary>
	/// UM TOQUE, com quadros no meio: o `LocalPlayer` le a tecla por `IsActionJustPressed` no `_Process`, e apertar e soltar no mesmo quadro
	/// poderia nem chegar a ser visto.
	/// </summary>
	private async Tarefa Toque(Key k)
	{
		Tecla(k, true);
		await Quadros(3);
		Tecla(k, false);
		await Quadros(2);
	}

	/// <summary>O C DUPLO: o `LerTeclaC` manda o `SendTransformar(true)` no segundo toque dentro da janela. Devolve a forma em que ficou.</summary>
	private async System.Threading.Tasks.Task<string> CDuplo()
	{
		string antes = Forma();
		await Toque(Key.C);
		await Segundos(0.15);
		await Toque(Key.C);
		await Ate(() => Forma() != antes, 4);
		return Forma();
	}

	/// <summary>O X: o `ReverterOuLiberar` do servidor. Espera a cena da descida acabar e devolve a forma em que ficou.</summary>
	private async System.Threading.Tasks.Task<string> TeclaX()
	{
		string antes = Forma();
		await Toque(Key.X);
		await Ate(() => Forma() != antes, 4);
		await AteACenaAcabar();
		return Forma();
	}

	// =====================================================================
	// O QUE SE LE
	// =====================================================================
	private int Eu => C?.LocalId ?? 0;

	private (string Classe, string Forma, List<string> Corpos, bool Travado, double Maestria, double Cena, bool Carregando)? Estado() =>
		S?.PalcoM_Estado(Eu);

	private string Forma() => Estado()?.Forma ?? "";

	private static string Painel() => C?.Sheet.Raciais ?? "";

	private static CharacterVisual? VisualDe(int id) => World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>O vermelho do descontrole -- a chama, os aneis e a aura colada -- e nao o azul-gelo das formas do Frost.</summary>
	private static bool Vermelho(Color c) => c.R > 0.8f && c.G < 0.45f && c.B < 0.4f;

	private bool ColadaVermelha() => VisualDe(Eu)?.ColadasNoCorpoDeTeste.Any(c => Vermelho(c.Tinta)) == true;

	private string Coladas() => VisualDe(Eu) is { } v
		? string.Join(", ", v.ColadasNoCorpoDeTeste.Select(c => $"{c.Folha.Split('/')[^1]} #{c.Tinta.ToHtml(false)}"))
		: "sem boneco";

	/// <summary>A cinematica que esta tocando no mundo agora, ou nula.</summary>
	private static Transformacao? CenaNoAr() =>
		World.Instancia is { } w ? Todos<Transformacao>(w).FirstOrDefault(t => t.FimDeTeste == Transformacao.FimDaCena.Rodando) : null;

	private async System.Threading.Tasks.Task<Transformacao?> CenaQueComecou()
	{
		await Ate(() => CenaNoAr() != null, 4);
		return CenaNoAr();
	}

	/// <summary>Espera a cena acabar nas duas pontas: o boneco solto no cliente e o prazo zerado no servidor.</summary>
	private async Tarefa AteACenaAcabar()
	{
		await Ate(() => CenaNoAr() == null && !Transformacao.PrendendoOCorpo && Estado()?.Cena <= 0, 45);
		await Segundos(0.3);
	}

	private static string Cena(Transformacao? t) => t is { } c
		? $"cena {(c.CenaDeTeste == Cinematicas.FrostDescontrole ? "do DESCONTROLE" : c.CenaDeTeste == Cinematicas.FrostSurtoDoDescontrole ? "do SURTO vermelho" : c.CenaDeTeste.Forma)}, "
		  + $"aneis #{c.CorDosAneisDeTeste.ToHtml(false)}, chama #{c.CorDaChamaDaCenaDeTeste.ToHtml(false)}"
		: "sem cena";

	private static string Banho((float Mistura, Color Cor, float Achatar)? b) =>
		b is { } x ? $"mistura {x.Mistura:0.00}, #{x.Cor.ToHtml(false)}" : "sem material";

	private static string Curto(string s)
	{
		string uma = s.Replace("\n", " | ");
		return uma.Length > 140 ? uma[..140] + "..." : uma;
	}

	private static IEnumerable<T> Todos<T>(Node raiz) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t) yield return t;
			foreach (T n in Todos<T>(f)) yield return n;
		}
	}

	private static Button? Botao(Node? raiz, string texto) =>
		raiz == null ? null : Todos<Button>(raiz).FirstOrDefault(b => b.Text == texto && b.IsVisibleInTree());

	private static void Apertar(Button? b) => b?.EmitSignal(BaseButton.SignalName.Pressed);

	private async System.Threading.Tasks.Task<T?> Achar<T>(Node raiz, Func<T, bool> quer, double segundos) where T : Node
	{
		T? achado = null;
		await Ate(() => (achado = Todos<T>(raiz).FirstOrDefault(quer)) != null, segundos);
		return achado;
	}

	// =====================================================================
	// O TEMPO E A FOTO
	// =====================================================================
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

	private void Fotografar(string nome)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
			img.SavePng(caminho);
			_fotos.Add(caminho);
		}
		catch (Exception e) { _linhas.Add("  --     sem foto: " + e.Message); }
	}

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print(Prefixo + l);
		foreach (string f in _fotos) GD.Print(Prefixo + "  foto " + f);
		GD.Print(_falhas.Count == 0 ? Prefixo + "===== TUDO OK =====" : $"{Prefixo}===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print(Prefixo + "  - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
