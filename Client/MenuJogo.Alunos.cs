using System.Text;
using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Verbos;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA "ALUNOS" / "MESTRE" -- o discipulado de quem joga: os alunos que ele guia e o progresso de cada um NO QUE
/// ELE ENSINOU; o mestre dele, o que o mestre lhe ensinou, e como ele vai perto dos outros alunos do mesmo mestre.
///
/// ============================ O PEDIDO DO DONO (2026-10-11) ============================
/// *"ter tipo uma aba people so q de alunos (caso vc seja o mestre) e mestre (caso vc seja aluno) ao qual vc pode
/// ver seus alunos e o quao fortes eles estao progredindo no q vc ensinou. e o aluno pode ver o mestre e os outros
/// alunos pra ver o quao bem ele ta progredindo em relacao aos outros alunos"*.
///
/// E UMA ABA PROPRIA, ao lado da People (e nao uma secao dela nem da Learning): a People e a lista de quem voce
/// CONHECE, com uma pergunta por pessoa; isto e outra lista, de outra gente, com outra coisa pra ler -- e a Learning
/// ja e a mais comprida do menu. O original tambem tinha aba propria (`ui_tab_alunos`, `MasterStudent.dm:596`), e e
/// dele o costume do NOME: "Alunos" pra quem guia alguem, "Mestre" pra quem so e guiado (`mst_tab_sync`, `:152`);
/// quem e as duas coisas fica com "Alunos", que mostra as duas partes.
///
/// ELA SO EXISTE PRA QUEM TEM O VINCULO -- pela tabela de requisitos (`RequisitosDosVerbos`: o verb `alunos`), com os
/// fatos que o servidor conta (`Fatos.TemMestre`, `Fatos.TemAluno`). E quem decide o que ha pra desenhar e o
/// servidor: o painel chega pronto (`GameClient.MestreEAlunos`), ja cortado pelo que este jogador pode ver.
///
/// ============================ O QUE ELA DESENHA ============================
///   * QUEM GUIA: um cartao por aluno (retrato, raca, no mundo ou visto por ultimo, aluno desde quando), o progresso
///     geral dele e cada ensinamento -- a forma (despertou? maestria) e a licao (nivel) -- com barra; os dois gestos
///     que ja existiam (ajudar a transformar, dispensar) apontados pra AQUELE aluno; e, depois dos cartoes, quem
///     esta mais adiantado em cada ensinamento.
///   * QUEM E GUIADO: o cartao do mestre com o que ele ensinou, o progresso em cada coisa e a posicao entre os
///     colegas ("2º de 4"); e um cartao por colega com as barras lado a lado nos ensinamentos em comum.
///
/// ============================ A FORCA ============================
/// Nenhum numero de poder, de ninguem (dono: *"o mestre nao pode ver o bp dos alunos ... oq vc sabe e se alguem e
/// mais forte ou mais fraco q vc em porcentagem"*). O que o cartao do aluno e o do mestre mostram e a MESMA linha
/// "poder relativo" da aba Sense (<see cref="LinhaDoPoderRelativo"/>), lida da mesma lista (`GameClient.Sentidos`)
/// e so quando a aba Sense a mostraria: quem tem a skill, sem scouter, com a pessoa ao alcance do sentido. Fora
/// disso: o motivo que e do JOGADOR (falta a skill, ou o scouter esta ligado) vai escrito UMA vez, no alto da
/// pagina; o que e da PESSOA (ela esta no mundo e o sentido nao a le) vai no cartao dela; e quem nao esta no mundo
/// ja tem a pilula OFFLINE dizendo. Cartao de colega nao tem linha de forca nenhuma.
///
/// O MENU E UMA JANELA (`MenuJogo.Janela.cs`): os cartoes moram numa fila que quebra linha (`HFlowContainer`) e
/// esticam -- um por linha no menu minimo, dois ou tres lado a lado na tela inteira, sem remontar a pagina.
/// </summary>
public partial class MenuJogo
{
	/// <summary>Os dois nomes da aba (ver o cabecalho). As bancadas a procuram por aqui.</summary>
	internal const string AbaDeAlunos = "Alunos", AbaDeMestre = "Mestre";

	/// <summary>O verb do pedido do painel -- a linha da tabela de requisitos que decide se a aba existe.</summary>
	internal const string VerboDoDiscipulado = "alunos";

	/// <summary>A largura minima de um cartao de pessoa: cabe no menu minimo (520), e dois cabem lado a lado a partir de ~900.</summary>
	private const float LarguraMinimaDoCartaoDeAluno = 400f;

	/// <summary>A largura minima de um cartao do conjunto ("quem esta mais adiantado"): dois cabem lado a lado no menu de fabrica (760).</summary>
	private const float LarguraMinimaDoRanking = 300f;

	/// <summary>Quanto tempo o segundo clique de um gesto sem volta (dispensar, deixar o mestre) fica valendo, em segundos.</summary>
	private const double SegundosPraConfirmar = 4;

	private static bool EhAbaDoDiscipulado(string aba) => aba is AbaDeAlunos or AbaDeMestre;

	// =====================================================================
	// A ABA NA BARRA
	// =====================================================================
	/// <summary>
	/// POE A ABA NA BARRA, logo depois da People -- se a tabela de requisitos diz que este personagem a tem. O nome
	/// segue o papel; quem estava na aba quando o papel mudou (o primeiro aluno aceitou, o ultimo foi embora e
	/// sobrou o mestre) continua nela, com o nome novo -- o `statsUItab = quero` do original.
	/// </summary>
	private void PorAAbaDoDiscipulado(List<string> abas)
	{
		IPosse posse = PosseDoCliente.Atual;
		if (!RequisitosDosVerbos.DoVerbo(VerboDoDiscipulado).Atende(posse)) return;

		string nome = (posse.Fatos & Fatos.TemAluno) != 0 ? AbaDeAlunos : AbaDeMestre;
		int people = abas.IndexOf("People");
		abas.Insert(people >= 0 ? people + 1 : abas.Count, nome);
		if (EhAbaDoDiscipulado(_aba) && _aba != nome) _aba = nome;
	}

	/// <summary>O painel chegou (ou saiu). So redesenha com a aba dele aberta, como o `AoConhecidos`.</summary>
	private void AoMestreEAlunos() { if (Visible && EhAbaDoDiscipulado(_aba)) Redesenhar(); }

	// =====================================================================
	// O PEDIDO: ao abrir, e enquanto aberta
	// =====================================================================
	private Godot.Timer? _relogioDosAlunos;
	private bool _alunosNaTela;
	private ulong _ultimoPedidoDeAlunosMs;

	/// <summary>A versao do que o cliente guardava quando a pagina VAZIA pediu o painel: enquanto nao mudar, a resposta esta a caminho.</summary>
	private int _painelPedidoNaVersao = -1;

	/// <summary>
	/// O RELOGIO DA ABA, criado na primeira vez em que ela e desenhada (quem nunca a abre nao tem relogio). Filho do
	/// menu: morre com ele, e a assinatura e do filho pro pai -- nao sobra ninguem escutando.
	/// </summary>
	private void GarantirORelogioDosAlunos()
	{
		if (_relogioDosAlunos != null && IsInstanceValid(_relogioDosAlunos)) return;
		_relogioDosAlunos = new Godot.Timer { Name = "RelogioDosAlunos", WaitTime = 0.5, Autostart = true };
		_relogioDosAlunos.Timeout += AoRelogioDosAlunos;
		AddChild(_relogioDosAlunos);
	}

	/// <summary>
	/// DUAS VEZES POR SEGUNDO, e so com a aba na tela: pergunta ao servidor se o painel mudou (ao abrir, e depois a
	/// cada <see cref="ProgressoDeAluno.SegundosEntrePedidos"/> s -- ele so responde se mudou) e deixa a pagina se
	/// conferir (os "ha quanto tempo", a leitura do sentido de Ki e a confirmacao armada andam com o relogio; quem
	/// decide se ha o que refazer e a assinatura da aba).
	/// </summary>
	private void AoRelogioDosAlunos()
	{
		bool naTela = Visible && EhAbaDoDiscipulado(_aba) && _busca.Text.Trim().Length == 0;
		bool acabouDeAbrir = naTela && !_alunosNaTela;
		_alunosNaTela = naTela;
		if (!naTela || GameClient.Instance is not { } cli) return;

		ulong agora = Time.GetTicksMsec();
		double espera = acabouDeAbrir ? 0.4 : ProgressoDeAluno.SegundosEntrePedidos;
		if (agora - _ultimoPedidoDeAlunosMs >= espera * 1000)
		{
			_ultimoPedidoDeAlunosMs = agora;
			cli.PedirMestreEAlunos(seguir: cli.MestreEAlunos != null);
		}
		Redesenhar();
	}

	/// <summary>A pagina sem painel pede um -- UMA vez por estado do que o cliente guarda, e nao uma por redesenho.</summary>
	private void PedirOPainelUmaVez(GameClient cli)
	{
		if (_painelPedidoNaVersao == cli.VersaoDeMestreEAlunos) return;
		_painelPedidoNaVersao = cli.VersaoDeMestreEAlunos;
		_ultimoPedidoDeAlunosMs = Time.GetTicksMsec();
		cli.PedirMestreEAlunos(seguir: false);
	}

	/// <summary>Ha quantos segundos o painel de agora chegou: e o que se soma aos "ha quanto tempo" que vieram nele.</summary>
	private static uint SegundosDesdeQueOPainelChegou(GameClient cli) =>
		(uint)Math.Min((Time.GetTicksMsec() - cli.MestreEAlunosChegouEmMs) / 1000, uint.MaxValue / 2);

	// =====================================================================
	// A PAGINA
	// =====================================================================
	private void AbaDoDiscipulado()
	{
		if (GameClient.Instance is not { } cli) return;
		GarantirORelogioDosAlunos();

		if (cli.MestreEAlunos is not { } painel)
		{
			PedirOPainelUmaVez(cli);
			VBoxContainer espera = Cartao("Mestre e alunos");
			Nota("buscando o seu discipulado...", espera).SetMeta("discipulado", "buscando");
			return;
		}

		uint passou = SegundosDesdeQueOPainelChegou(cli);
		bool guio = painel.Alunos.Count > 0;

		// A FAIXA: o numero desta aba, e a frase que diz o que ela mostra.
		if (guio)
			Faixa("Alunos", $"{painel.Alunos.Count} de {Discipulado.MaxAlunos}",
				  "quem você guia  ·  o progresso de cada um no que VOCÊ ensinou");
		else if (painel.Mestre is { } so)
			Faixa("Mestre", so.Quem.Nome, "o que ele te ensinou  ·  e como você vai perto dos outros alunos dele");
		else
		{
			VBoxContainer vazio = Cartao("Mestre e alunos");
			Nota("Você não tem mestre nem alunos agora.", vazio).SetMeta("discipulado", "vazio");
			return;
		}

		// A FORCA, quando o motivo de nao haver leitura e do JOGADOR (e por isso vale pra pagina inteira): escrito UMA
		// vez, aqui -- repetido em cada cartao ele viraria o assunto de uma tela que e sobre o que foi ensinado.
		bool sense = _atributos.Tem(Protocol.Poder.Sense), scouter = _atributos.Tem(Protocol.Poder.Scouter) || cli.SentidosSaoDoScouter;
		if (FraseDaForcaNaPagina(sense, scouter) is { Length: > 0 } daPagina)
			Nota(daPagina, _conteudo).SetMeta("forca_da_pagina", CasoSemLeitura(true, sense, scouter));

		if (guio)
		{
			Secao("Seus alunos");
			HFlowContainer fila = FilaDeCartoes();
			foreach (AlunoNoPainel a in painel.Alunos) CartaoDeAluno(cli, a, passou, fila);
			ConjuntoDosAlunos(painel.Alunos);
		}

		if (painel.Mestre is { } m)
		{
			Secao("Seu mestre");
			CartaoDoMestre(cli, m, passou);

			Secao($"Os outros alunos de {m.Quem.Nome}");
			if (m.Colegas.Count == 0)
				Aviso("Por enquanto você é o único aluno dele. Quando houver outros, o progresso deles nos mesmos "
					  + "ensinamentos aparece aqui, ao lado do seu.");
			else
			{
				HFlowContainer fila = FilaDeCartoes();
				for (int i = 0; i < m.Colegas.Count; i++) CartaoDeColega(m, i, fila);
			}
		}
	}

	/// <summary>A fila em que os cartoes de pessoa moram: quebra linha, e cada cartao estica na linha dele.</summary>
	private HFlowContainer FilaDeCartoes()
	{
		var fila = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		fila.AddThemeConstantOverride("h_separation", 8);
		fila.AddThemeConstantOverride("v_separation", 8);
		_conteudo.AddChild(fila);
		return fila;
	}

	/// <summary>Um cartao de pessoa numa fila: marcado com o tipo (`aluno`, `colega`) e o nome, e com a largura minima.</summary>
	private VBoxContainer CartaoNaFila(string tipo, string nome, Control fila)
	{
		VBoxContainer corpo = Cartao("", fila);
		MarcarCartaoDeItem(corpo, tipo, nome);
		corpo.AddThemeConstantOverride("separation", 5);
		if (corpo.GetParent() is Control cartao) cartao.CustomMinimumSize = new Vector2(LarguraMinimaDoCartaoDeAluno, 0);
		return corpo;
	}

	// =====================================================================
	// QUEM GUIA: o conjunto e o cartao de um aluno
	// =====================================================================
	/// <summary>O titulo da secao de conjunto. As bancadas a procuram por aqui.</summary>
	internal const string TituloDoConjunto = "Quem está mais adiantado";

	/// <summary>
	/// QUEM ESTA MAIS ADIANTADO EM CADA ENSINAMENTO: um cartao pequeno por coisa ensinada, com quem a recebeu do
	/// primeiro ao ultimo (`ProgressoDeAluno.Conjunto` -- a mesma conta que a bancada do servidor confere). Vem DEPOIS
	/// dos cartoes dos alunos (quem abre a aba ve gente primeiro; isto e o resumo) e so existe com DOIS alunos ou mais
	/// e algum ensinamento: de um aluno so nao ha quem comparar. O metadado `ranking` leva a chave do ensinamento.
	/// </summary>
	private void ConjuntoDosAlunos(IReadOnlyList<AlunoNoPainel> alunos)
	{
		if (alunos.Count < 2) return;
		List<RankingDoEnsinamento> conjunto = ProgressoDeAluno.Conjunto(alunos);
		if (conjunto.Count == 0) return;

		Secao(TituloDoConjunto);
		HFlowContainer fila = FilaDeCartoes();
		foreach (RankingDoEnsinamento r in conjunto)
		{
			string nome = NomeDoEnsinamento(r.Tipo, r.Id);
			VBoxContainer corpo = Cartao("", fila);
			MarcarCartaoDeItem(corpo, "ranking", nome);
			corpo.AddThemeConstantOverride("separation", 3);
			if (corpo.GetParent() is Control cartao)
			{
				cartao.CustomMinimumSize = new Vector2(LarguraMinimaDoRanking, 0);
				cartao.SetMeta("ranking", ChaveDoEnsinamento(r.Tipo, r.Id));
			}
			corpo.AddChild(Cabecalho(nome, Tema.Texto, PilulaDoTipo(r.Tipo)));
			foreach (LugarNoRanking l in r.Lugares)
				LinhaDoProgresso(r.Tipo, l.Progresso, corpo, $"{l.Posicao}º  {alunos[l.Aluno].Quem.Nome}");
		}
	}

	private void CartaoDeAluno(GameClient cli, AlunoNoPainel a, uint passou, Control fila)
	{
		VBoxContainer corpo = CartaoNaFila("aluno", a.Quem.Nome, fila);
		corpo.AddChild(TopoDePessoa(cli, a.Quem, passou, "seu aluno", [.. a.Ensinamentos.Select(e => e.Progresso)],
									"Progresso no que você ensinou"));

		if (a.Ensinamentos.Count == 0)
			// O VINCULO DE ANTES DO DIARIO (sem data) pode ter ensinamento que ninguem anotou: a frase nao afirma que nao houve.
			Nota((a.Quem.DesdeHaSegundos > 0
					 ? $"Você ainda não ensinou nada a {a.Quem.Nome}. "
					 : $"O diário deste vínculo começa agora: o que você ensinou a {a.Quem.Nome} antes disso não ficou anotado. ")
				 + "De frente pra ele: \"Ajudar a transformar\" (aqui embaixo) desperta uma forma com a porta de poder pela metade, "
				 + "e \"Ensinar…\" passa uma skill ou uma técnica de ki sua.",
				 corpo).SetMeta("discipulado", "sem-ensinamentos");
		else
		{
			corpo.AddChild(Tema.Rotulo("O que você ensinou"));
			foreach (EnsinamentoNoPainel e in a.Ensinamentos) BlocoDeEnsinamento(e, passou, "COM VOCÊ", "você tentou", null, corpo);
		}

		// OS DOIS GESTOS QUE JA EXISTIAM, apontados pra ESTE aluno. Quem valida e o servidor (`GameServer.Alunos.cs`).
		string sig = a.Quem.Assinatura, nome = a.Quem.Nome;
		HFlowContainer botoes = FilaDeBotoes(corpo);
		var ajudar = new Button
		{
			Text = "Ajudar a transformar", FocusMode = Control.FocusModeEnum.None, Disabled = !a.Quem.Online,
			TooltipText = a.Quem.Online
				? $"Provoca (ou guia) {nome} até uma transformação: fique DE FRENTE pra ele. O poder mínimo que a forma exige "
				  + $"dele cai pela metade, e você fica {Discipulado.RecargaDeEnsinoSegundos / 60:0} min sem fôlego pra tentar de novo."
				: $"{nome} não está no mundo agora.",
		};
		ajudar.SetMeta("acao", "ensinar");
		ajudar.AddThemeFontSizeOverride("font_size", 12);
		ajudar.Pressed += () => cli.AjudarAlunoATransformar(sig);
		botoes.AddChild(ajudar);

		// "ENSINAR...": abre (e fecha) a lista do que passar a ESTE aluno, aqui mesmo no cartao -- ver `ListaDoQueEnsinar`.
		bool aberta = _ensinandoA == sig && a.Quem.Online;
		var ensinar = new Button
		{
			Text = aberta ? "Fechar a lista" : "Ensinar…", FocusMode = Control.FocusModeEnum.None, Disabled = !a.Quem.Online,
			TooltipText = a.Quem.Online
				? $"Escolhe uma skill ou uma técnica de ki sua pra ensinar a {nome}: fique COLADO nele. Ele aceita ou recusa pelo aviso que aparece na tela."
				: $"{nome} não está no mundo agora.",
		};
		ensinar.SetMeta("acao", "licao");
		ensinar.SetMeta("aberta", aberta);
		ensinar.AddThemeFontSizeOverride("font_size", 12);
		ensinar.Pressed += () =>
		{
			_ensinandoA = _ensinandoA == sig ? "" : sig;
			_rolarAteALista = _ensinandoA.Length > 0;
			Redesenhar();
		};
		botoes.AddChild(ensinar);
		botoes.AddChild(BotaoDeConfirmar("Dispensar aluno", $"Confirmar: dispensar {nome}", "dispensar:" + sig, "dispensar",
										 $"Desfaz o vínculo com {nome} (vale de qualquer lugar, com ele no mundo ou não). O que você "
										 + "ensinou fica com ele; este cartão some.",
										 () => cli.DispensarAluno(sig)));

		if (aberta) ListaDoQueEnsinar(cli, sig, nome, corpo);
	}

	// =====================================================================
	// "ENSINAR...": o que o mestre passa a ESTE aluno
	// =====================================================================
	/// <summary>De qual aluno a lista do "Ensinar..." esta aberta (a assinatura dele), ou vazio. Uma de cada vez.</summary>
	private string _ensinandoA = "";

	/// <summary>A altura da lista do "Ensinar..." quando ela e comprida: ai ela rola por dentro, pra um mestre de cem skills nao esticar o cartao.</summary>
	private const float AlturaDaListaDeEnsino = 190f;

	/// <summary>Ate quantas licoes a lista cabe solta no cartao (umas tres linhas de botoes); acima disso ela ganha a rolagem propria.</summary>
	private const int LicoesSemRolagem = 9;

	/// <summary>
	/// A lista ACABOU de abrir pelo clique: a pagina rola ate ela. Ela nasce embaixo dos botoes do cartao, e num
	/// cartao comprido isso e abaixo da dobra do menu -- o clique parecia nao ter feito nada. So no clique: a pagina se
	/// refaz por outros motivos com a lista aberta, e puxar a rolagem a cada vez tiraria a tela da mao do jogador.
	/// </summary>
	private bool _rolarAteALista;

	/// <summary>DEFEITO INJETADO (bancada `--diagalunos`): a lista abre e a pagina nao rola ate ela -- o primeiro desenho. Falso em jogo, sempre.</summary>
	internal static bool ListaAbreSemRolarDeTeste;

	/// <summary>
	/// A LISTA DO "ENSINAR...", dentro do cartao do aluno (dono, 2026-10-13: *"dar pra ensinar mais skills pros alunos e
	/// tb técnicas proprias"*): as tecnicas de ki que o mestre inventou e tudo o que ele sabe
	/// (`EnsinoDeSkill.OQueOMestreEnsina`). Cada botao e a OFERTA -- o aluno responde pelo aviso de tela, e quem decide
	/// e o servidor (`GameServer.LicaoPeloCartao`): colado nele, fora da recarga, e o que foi ensinado ao mestre nao
	/// repassa (a tela nao sabe o que foi ensinado a ele: a recusa diz).
	///
	/// A ESTRELA marca a LICAO DE MESTRE -- a skill que nao se ensina a um estranho (`EnsinoDeSkill.LicaoDeMestre`).
	/// So ela GASTA os marcos do aluno; as outras so exigem que ele os tenha. O numero no botao e esse preco.
	/// </summary>
	private void ListaDoQueEnsinar(GameClient cli, string sig, string nome, Control corpo)
	{
		var bloco = new VBoxContainer();
		bloco.SetMeta("discipulado", "ensinar");
		bloco.AddThemeConstantOverride("separation", 4);
		corpo.AddChild(bloco);
		bloco.AddChild(Tema.Rotulo($"O que ensinar a {nome}"));
		if (_rolarAteALista)
		{
			_rolarAteALista = false;
			// (um instante depois: a rolagem so sabe onde o bloco fica quando a pagina acabou de se arrumar)
			if (!ListaAbreSemRolarDeTeste)
				GetTree().CreateTimer(0.05).Timeout += () => { if (IsInstanceValid(bloco)) _rolagemDaPagina.EnsureControlVisible(bloco); };
		}

		var botoes = new List<Button>();
		foreach (TecnicaCustomizada t in cli.Customizadas)
		{
			if (!t.Criada || t.Ensinada) continue;
			int id = t.Id;
			Button b = BotaoDeLicao($"Técnica de ki: {t.Nome}",
				$"Ensina a {nome} a técnica que você inventou. Ele precisa já ter aberto esse tipo de técnica e ter uma vaga entre as "
				+ $"{TecnicaCustomizada.Maximo} dele. Não custa marco nenhum, e ele não pode repassá-la.");
			b.SetMeta("tecnica", id);
			b.Pressed += () => cli.EnsinarTecnicaAoAluno(sig, id);
			botoes.Add(b);
		}

		if (CatalogoPublico() is { } cat)
			foreach (Skill s in EnsinoDeSkill.OQueOMestreEnsina(cat, cli.SkillsAprendidas).OrderBy(s => s.Nome, StringComparer.OrdinalIgnoreCase))
			{
				bool deMestre = EnsinoDeSkill.LicaoDeMestre(s, eMeuAluno: true);
				string path = s.Path;
				int custo = EnsinoDeSkill.Custo(s);
				Button b = BotaoDeLicao($"{(deMestre ? "★ " : "")}{s.Nome} ({custo})", deMestre
					? $"LIÇÃO DE MESTRE: {s.Nome} custa {custo} marco(s) DELE. Vale sem o pré-requisito e sem o patamar -- mas tem que ser "
					  + "uma skill que a raça e a classe dele alcançam. Ele não pode repassá-la."
					: $"{s.Nome}: ele precisa ter {custo} marco(s) livre(s), e não gasta nenhum. Ele não pode repassá-la.");
				b.SetMeta("licao", path);
				b.SetMeta("de_mestre", deMestre);
				b.Pressed += () => cli.EnsinarSkillAoAluno(sig, path);
				botoes.Add(b);
			}

		if (botoes.Count == 0)
		{
			Nota("Você ainda não sabe nada que dê pra ensinar.", bloco).SetMeta("discipulado", "nada-a-ensinar");
			return;
		}

		Nota($"Fique colado nele: a oferta aparece na tela dele, com Aceitar e Recusar ({EnsinoDeSkill.PrazoDaOfertaSegundos:0} s). "
			 + "O número é o que a lição exige em marcos. ★ = lição de mestre (o que você não ensinaria a um estranho): essa GASTA os "
			 + "marcos dele; as outras só exigem que ele os tenha.", bloco);

		var lista = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		lista.AddThemeConstantOverride("h_separation", 6);
		lista.AddThemeConstantOverride("v_separation", 4);
		foreach (Button b in botoes) lista.AddChild(b);

		// CURTA, ela vai solta no cartao; comprida, numa caixa que rola por dentro (a caixa de altura fixa em volta de
		// dois botoes era um vazio de meio cartao).
		if (botoes.Count <= LicoesSemRolagem) { bloco.AddChild(lista); return; }
		var rolagem = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(0, AlturaDaListaDeEnsino),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		bloco.AddChild(rolagem);
		rolagem.AddChild(lista);
	}

	private static Button BotaoDeLicao(string rotulo, string dica)
	{
		var b = new Button { Text = rotulo, TooltipText = dica, FocusMode = Control.FocusModeEnum.None };
		b.AddThemeFontSizeOverride("font_size", 12);
		return b;
	}

	// =====================================================================
	// QUEM E GUIADO: o cartao do mestre e os dos colegas
	// =====================================================================
	private void CartaoDoMestre(GameClient cli, MestreNoPainel m, uint passou)
	{
		VBoxContainer corpo = Cartao("");
		MarcarCartaoDeItem(corpo, "mestre", m.Quem.Nome);
		corpo.AddThemeConstantOverride("separation", 5);
		corpo.AddChild(TopoDePessoa(cli, m.Quem, passou, "seu mestre", [.. m.Ensinamentos.Select(e => e.Meu.Progresso)],
									"Seu progresso no que ele ensinou"));

		// A REGRA DO TREINO, com os numeros que o servidor usa (`GainKnobs`): e a linha que a aba do original tinha.
		Nota($"Treinar lutando contra o seu mestre rende de {Jandirus.Core.Stats.GainKnobs.MasterGainMin:0.#}× a "
			 + $"{Jandirus.Core.Stats.GainKnobs.MasterGainCap:0.#}× mais poder por golpe enquanto ele for mais forte que você (quanto mais "
			 + "forte, mais) — quem supera o mestre perde esse bônus. Contra um "
			 + $"colega — outro aluno dele — {Jandirus.Core.Stats.GainKnobs.ColegaDeMestreGain:0.#}×.", corpo);

		if (m.Ensinamentos.Count == 0)
			Nota((m.Quem.DesdeHaSegundos > 0
					 ? "Seu mestre ainda não te ensinou nada. "
					 : "O diário deste vínculo começa agora: o que ele te ensinou antes disso não ficou anotado. ")
				 + "Quando ele te ajudar a despertar uma forma ou te passar uma técnica, o seu progresso em cada coisa aparece aqui.",
				 corpo).SetMeta("discipulado", "sem-ensinamentos");
		else
		{
			corpo.AddChild(Tema.Rotulo("O que ele te ensinou"));
			foreach (EnsinamentoDoMestre e in m.Ensinamentos)
				BlocoDeEnsinamento(e.Meu, passou, "COM O MESTRE", "ele tentou",
								   e.Colegas.Count > 0 ? ProgressoDeAluno.Posicao(e.Meu.Progresso, e.Colegas) : null, corpo);
		}

		HFlowContainer botoes = FilaDeBotoes(corpo);
		botoes.AddChild(BotaoDeConfirmar("Deixar meu mestre", $"Confirmar: deixar {m.Quem.Nome}", "largar", "largar",
										 "Segue o próprio caminho. O ganho extra de treinar contra ele acaba junto; o que ele te ensinou fica.",
										 () => cli.SendHabilidade("mst_largar")));
	}

	/// <summary>
	/// O CARTAO DE UM COLEGA: o nome, o retrato (se eu ja o vi) e, em cada ensinamento que NOS DOIS recebemos, as duas
	/// barras lado a lado -- a dele e a minha. Do colega nao chega mais nada: nem o que o mestre ensinou so a ele.
	/// </summary>
	private void CartaoDeColega(MestreNoPainel m, int indice, Control fila)
	{
		string nome = m.Colegas[indice];
		var emComum = new List<(EnsinamentoNoPainel Meu, ProgressoNoEnsinamento Dele)>();
		foreach (EnsinamentoDoMestre e in m.Ensinamentos)
			foreach (ColegaNoEnsinamento c in e.Colegas)
				if (c.Colega == indice) { emComum.Add((e.Meu, c.Progresso)); break; }

		VBoxContainer corpo = CartaoNaFila("colega", nome, fila);

		var topo = new HBoxContainer();
		topo.AddThemeConstantOverride("separation", 10);
		topo.AddChild(Retrato(nome, EscalaDoRetrato));
		var ficha = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		ficha.AddThemeConstantOverride("separation", 3);
		ficha.AddChild(Titulo(nome));
		if (emComum.Count == 0)
			Nota("Nenhum ensinamento em comum com você ainda.", ficha).SetMeta("discipulado", "sem-comum");
		else
		{
			double dele = ProgressoDeAluno.Geral(emComum.Select(x => x.Dele)).Media, meu = ProgressoDeAluno.Geral(emComum.Select(x => x.Meu.Progresso)).Media;
			Nota($"{emComum.Count} ensinamento{(emComum.Count == 1 ? "" : "s")} em comum  ·  {nome} {dele * 100:0}%  ·  você {meu * 100:0}%", ficha)
				.SetMeta("discipulado", "em-comum");
		}
		topo.AddChild(ficha);
		corpo.AddChild(topo);

		foreach ((EnsinamentoNoPainel meu, ProgressoNoEnsinamento dele) in emComum)
		{
			var bloco = new VBoxContainer();
			bloco.SetMeta("ensinamento", ChaveDoEnsinamento(meu.Tipo, meu.Id));
			bloco.AddThemeConstantOverride("separation", 2);
			bloco.AddChild(Cabecalho(NomeDoEnsinamento(meu.Tipo, meu.Id), Tema.Texto, PilulaDoTipo(meu.Tipo)));

			// AS DUAS BARRAS, LADO A LADO: a do colega a esquerda, a minha a direita, do mesmo tamanho. Numa fila que
			// quebra linha -- no cartao estreito demais pras duas, a minha desce pra baixo da dele.
			var par = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			par.SetMeta("discipulado", "par");
			par.AddThemeConstantOverride("h_separation", 14);
			par.AddThemeConstantOverride("v_separation", 2);
			LinhaDoProgresso(meu.Tipo, dele, par, nome);
			LinhaDoProgresso(meu.Tipo, meu.Progresso, par, "você");
			IgualarAsMetades(par);
			bloco.AddChild(par);
			corpo.AddChild(bloco);
		}
	}

	/// <summary>A largura minima de cada metade do par de barras de um colega: as duas cabem lado a lado no cartao mais estreito (400).</summary>
	private const float LarguraMinimaDaMetadeDoPar = 180f;

	/// <summary>
	/// AS DUAS METADES DO PAR COM A MESMA LARGURA -- duas barras so se comparam de relance se tem o mesmo tamanho.
	/// Cada metade nasce do tamanho do texto dela ("Raditz ... ainda não despertou" e bem mais larga que "você ...
	/// maestria 30%"), e a fila reparte a sobra por igual: a mais comprida ficava com a barra maior. Aqui as duas
	/// ganham o MESMO minimo, e o nome (o rotulo da esquerda) passa a cortar com reticencias em vez de alargar a
	/// metade dele -- o nome inteiro ja esta no titulo do cartao.
	/// </summary>
	private static void IgualarAsMetades(HFlowContainer par)
	{
		foreach (Node n in par.GetChildren())
		{
			if (n is not Control metade) continue;
			metade.CustomMinimumSize = new Vector2(LarguraMinimaDaMetadeDoPar, 0);
			if (metade.GetChildCount() == 0 || metade.GetChild(0) is not HBoxContainer linha) continue;
			if (linha.GetChildCount() == 0 || linha.GetChild(0) is not Label dono) continue;
			dono.ClipText = true;
			dono.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		}
	}

	// =====================================================================
	// AS PECAS
	// =====================================================================
	/// <summary>
	/// O TOPO DE UM CARTAO DE PESSOA (aluno ou mestre): o retrato a esquerda (a aparencia como eu a vi por ultimo --
	/// o mesmo da aba People), e a direita o nome com a pilula de presenca, a raca e as datas, a leitura de forca e
	/// o progresso geral.
	/// </summary>
	private Control TopoDePessoa(GameClient cli, PessoaNoPainel q, uint passou, string papel,
								 IReadOnlyList<ProgressoNoEnsinamento> progressos, string rotuloDoGeral)
	{
		var topo = new HBoxContainer();
		topo.AddThemeConstantOverride("separation", 10);
		topo.AddChild(Retrato(q.Nome, EscalaDoRetrato));

		var ficha = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		ficha.AddThemeConstantOverride("separation", 3);
		ficha.AddChild(Cabecalho(q.Nome, Tema.Texto, q.Online ? ("ONLINE", Tema.Bom) : ("OFFLINE", Tema.TextoFraco)));
		Nota(QuemEDesdeQuando(q, passou, papel), ficha).SetMeta("discipulado", "datas");
		LinhaDeForca(cli, q, ficha);
		if (progressos.Count > 0)
		{
			double media = ProgressoDeAluno.Geral(progressos).Media;
			LinhaComBarra(rotuloDoGeral, $"{media * 100:0}%", media, Tema.Bom, ficha);
		}
		topo.AddChild(ficha);
		return topo;
	}

	/// <summary>"Saiyajin  ·  seu aluno há 12 dias  ·  visto por último há 2 h". So o que se sabe entra.</summary>
	internal static string QuemEDesdeQuando(PessoaNoPainel q, uint passou, string papel)
	{
		var partes = new List<string>(3);
		if (q.Raca.Length > 0) partes.Add(Jandirus.Core.Races.NomeDaRaca.Bonito(q.Raca));
		// "NAO SEI" NAO SE ESCREVE: o vinculo de antes de o diario existir nao tem data.
		partes.Add(q.DesdeHaSegundos > 0 ? $"{papel} {HaQuantoTempo(q.DesdeHaSegundos + passou)}" : papel);
		if (!q.Online && q.VistoHaSegundos > 0) partes.Add($"visto por último {HaQuantoTempo(q.VistoHaSegundos + passou)}");
		return string.Join("  ·  ", partes);
	}

	/// <summary>
	/// A LEITURA DE FORCA DE UM CARTAO: a linha "poder relativo" da aba Sense, se o meu sentido de Ki esta lendo esta
	/// pessoa agora (`GameClient.PoderRelativoSentido`). Numero de poder nao aparece aqui em caso nenhum, nem com
	/// scouter. O metadado `forca` diz em qual dos casos o cartao esta -- e so UM dos casos sem leitura vira frase
	/// no cartao: o que e desta pessoa (<see cref="CasoNaoSentida"/>).
	/// </summary>
	private void LinhaDeForca(GameClient cli, PessoaNoPainel q, Control pai)
	{
		if (ForcaSentida(cli, q) is { } porcento)
		{
			var caixa = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
			caixa.SetMeta("forca", "sentida");
			LinhaDoPoderRelativo(porcento, caixa);
			pai.AddChild(caixa);
			return;
		}

		string caso = CasoSemLeitura(q.Online, _atributos.Tem(Protocol.Poder.Sense),
									 _atributos.Tem(Protocol.Poder.Scouter) || cli.SentidosSaoDoScouter);
		// O MOTIVO QUE E DO JOGADOR ja esta escrito uma vez no alto da pagina, e quem nao esta no mundo tem a pilula
		// OFFLINE: nenhum dos dois se repete aqui. So o que e DESTA pessoa (ela esta ai, e o sentido nao a le) vira frase.
		if (caso == CasoNaoSentida) Nota(FraseDeNaoSentida, pai).SetMeta("forca", caso);
		else pai.SetMeta("forca", caso);
	}

	/// <summary>
	/// A leitura do meu sentido de Ki pra esta pessoa, ou nulo. Quem o painel diz que NAO esta no mundo nao tem
	/// leitura nem que a lista do sentido traga alguem que case com ela: nao se sente quem nao esta la.
	/// </summary>
	private static float? ForcaSentida(GameClient cli, PessoaNoPainel q) =>
		q.Online ? cli.PoderRelativoSentido(q.Assinatura, q.Nome) : null;

	/// <summary>
	/// O caso de uma pessoa que esta no mundo e cujo poder o meu sentido de Ki nao esta lendo agora: fora do alcance
	/// dele -- ou ao alcance, mas com um homonimo por perto (nao da pra dizer qual dos dois e).
	/// </summary>
	internal const string CasoNaoSentida = "nao-sentida";

	/// <summary>A frase desse caso, no cartao da pessoa.</summary>
	internal const string FraseDeNaoSentida = "força: o seu sentido de Ki não está lendo essa pessoa agora (é a mesma leitura da aba Sense).";

	/// <summary>
	/// POR QUE NAO HA LEITURA DE FORCA pra uma pessoa -- o caso (o metadado `forca` do cartao dela): "offline" (ela nao
	/// esta no mundo: nao ha como senti-la), "scouter" e "sem-sense" (os dois motivos que sao do JOGADOR), ou
	/// <see cref="CasoNaoSentida"/>.
	/// </summary>
	internal static string CasoSemLeitura(bool online, bool temSense, bool scouterLigado) =>
		!online ? "offline" : scouterLigado ? "scouter" : !temSense ? "sem-sense" : CasoNaoSentida;

	/// <summary>
	/// O MOTIVO QUE E DO JOGADOR, escrito UMA vez no alto da pagina ("" = nao ha nenhum: ele sente Ki e esta sem
	/// scouter). Nenhuma das duas frases tem numero.
	/// </summary>
	internal static string FraseDaForcaNaPagina(bool temSense, bool scouterLigado) =>
		scouterLigado ? "Força: com o scouter ligado a leitura de poder é a da aba Scan — esta tela não mostra número de poder."
		: !temSense ? "Força: você ainda não sabe sentir Ki (a skill Sense). Com ela, o cartão de quem estiver ao alcance mostra "
					  + "o poder dele perto do seu, em porcentagem — a mesma leitura da aba Sense."
		: "";

	/// <summary>A chave de um ensinamento nos metadados da pagina ("1:ssj1", "2:/datum/skill/...").</summary>
	internal static string ChaveDoEnsinamento(TipoDeEnsinamento tipo, string id) => $"{(byte)tipo}:{id}";

	/// <summary>O nome de um ensinamento como o resto do menu o escreve: a forma pelo catalogo de formas, a licao pelo de skills.</summary>
	internal static string NomeDoEnsinamento(TipoDeEnsinamento tipo, string id)
	{
		if (tipo == TipoDeEnsinamento.Forma)
			return Jandirus.Core.Forms.Catalogo.Def(id) is { } d ? Jandirus.Core.Forms.Catalogo.NomeDe(d, false) : id;
		if (CatalogoPublico()?.Get(id) is { Nome.Length: > 0 } s) return s.Nome;
		return id[(id.LastIndexOf('/') + 1)..].Replace('_', ' ');
	}

	private static (string Texto, Color Cor) PilulaDoTipo(TipoDeEnsinamento tipo) =>
		tipo == TipoDeEnsinamento.Forma ? ("FORMA", Tema.TextoFraco) : ("LIÇÃO", Tema.TextoFraco);

	/// <summary>O estado de um ensinamento numa pilula. `comQuem` e o complemento do despertar assistido ("COM VOCÊ", "COM O MESTRE").</summary>
	internal static (string Texto, Color Cor) PilulaDoEstado(EnsinamentoNoPainel e, string comQuem)
	{
		ProgressoNoEnsinamento p = e.Progresso;
		if (e.Tipo == TipoDeEnsinamento.Forma)
			return p.Etapa switch
			{
				EtapaDoEnsinamento.AindaNao => ("AINDA NÃO DESPERTOU", Tema.TextoFraco),
				EtapaDoEnsinamento.Dominado => ("DOMINADA", Tema.Destaque),
				_ => (e.Assistida ? "DESPERTOU " + comQuem : "DESPERTOU", Tema.Bom),
			};
		return p.Etapa switch
		{
			EtapaDoEnsinamento.AindaNao => ("ESQUECEU", Tema.Perigo),
			EtapaDoEnsinamento.Dominado when p.Teto > 0 => ("DOMINOU", Tema.Destaque),
			_ => ("APRENDEU", Tema.Bom),
		};
	}

	/// <summary>O progresso de um ensinamento em palavras e barra: (rotulo, valor, razao da barra, cor).</summary>
	internal static (string Rotulo, string Valor, double Razao, Color Cor) LeituraDoProgresso(TipoDeEnsinamento tipo, ProgressoNoEnsinamento p)
	{
		double razao = p.Etapa switch
		{
			EtapaDoEnsinamento.AindaNao => 0,
			EtapaDoEnsinamento.Dominado => 1,
			_ => p.Milesimos / 1000.0,
		};
		if (tipo == TipoDeEnsinamento.Forma)
			return ("maestria", p.Etapa switch
			{
				EtapaDoEnsinamento.AindaNao => "ainda não despertou",
				EtapaDoEnsinamento.Dominado => "100%",
				_ => $"{p.Milesimos / 10.0:0.#}%",
			}, razao, Tema.Destaque);

		return p.Etapa switch
		{
			EtapaDoEnsinamento.AindaNao => ("nível", "esqueceu a lição", razao, Tema.Ki),
			_ when p.Teto == 0 => ("lição", "aprendida (não tem níveis)", razao, Tema.Ki),
			_ => ("nível", $"{p.Nivel} de {p.Teto}", razao, Tema.Ki),
		};
	}

	/// <summary>
	/// O progresso num texto so, pra linha em que o rotulo e o NOME de alguem ("Gohan ........ maestria 62%"): a
	/// forma como "maestria 62%" / "dominada", a licao como "nível 2 de 3" / "aprendida".
	/// </summary>
	internal static string ProgressoEmUmaFrase(TipoDeEnsinamento tipo, ProgressoNoEnsinamento p)
	{
		if (tipo == TipoDeEnsinamento.Forma)
			return p.Etapa switch
			{
				EtapaDoEnsinamento.AindaNao => "ainda não despertou",
				EtapaDoEnsinamento.Dominado => "dominada",
				_ => $"maestria {p.Milesimos / 10.0:0.#}%",
			};
		return p.Etapa switch
		{
			EtapaDoEnsinamento.AindaNao => "esqueceu a lição",
			_ when p.Teto == 0 => "aprendida",
			EtapaDoEnsinamento.Dominado => $"nível {p.Teto} de {p.Teto}",
			_ => $"nível {p.Nivel} de {p.Teto}",
		};
	}

	/// <summary>
	/// Uma linha com barra do progresso de ALGUEM num ensinamento. Sem `dono` e a linha do proprio cartao ("maestria
	/// .... 62%"); com ele (um nome, "você", "2º  Fulano") o dono e o rotulo e o progresso vai inteiro no valor.
	/// </summary>
	private static void LinhaDoProgresso(TipoDeEnsinamento tipo, ProgressoNoEnsinamento p, Control pai, string dono = "")
	{
		(string rotulo, string valor, double razao, Color cor) = LeituraDoProgresso(tipo, p);
		if (dono.Length > 0) LinhaComBarra(dono, ProgressoEmUmaFrase(tipo, p), razao, cor, pai);
		else LinhaComBarra(rotulo, valor, razao, cor, pai);
	}

	/// <summary>
	/// UM ENSINAMENTO NUM CARTAO: o nome com as pilulas (o que e, em que pe esta e -- pro aluno -- a posicao entre os
	/// colegas), a barra, e a linha de quando foi. O metadado `ensinamento` leva a chave, e o `lugar` a posicao.
	/// </summary>
	private static void BlocoDeEnsinamento(EnsinamentoNoPainel e, uint passou, string comQuem, string quemTentou,
										   (int Posicao, int De, bool Empate)? lugar, Control pai)
	{
		var bloco = new VBoxContainer();
		bloco.SetMeta("ensinamento", ChaveDoEnsinamento(e.Tipo, e.Id));
		bloco.AddThemeConstantOverride("separation", 2);

		(string Texto, Color Cor) doLugar = lugar is { } l
			? ($"{l.Posicao}º de {l.De}{(l.Empate ? " · empate" : "")}", l.Posicao == 1 ? Tema.Destaque : Tema.Texto)
			: ("", Tema.Texto);
		if (lugar is { } posto) bloco.SetMeta("lugar", $"{posto.Posicao}/{posto.De}");

		// O NOME E AS PILULAS NUMA FILA SO, que quebra linha: no cartao estreito (o menu minimo) as pilulas descem
		// pra baixo do nome em vez de empurrar a largura do cartao.
		var topo = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		topo.AddThemeConstantOverride("h_separation", 6);
		topo.AddThemeConstantOverride("v_separation", 2);
		Label nome = Titulo(NomeDoEnsinamento(e.Tipo, e.Id), Tema.Texto, 14);
		nome.AutowrapMode = TextServer.AutowrapMode.Off;
		topo.AddChild(nome);
		foreach ((string texto, Color cor) in new[] { PilulaDoTipo(e.Tipo), PilulaDoEstado(e, comQuem), doLugar })
		{
			if (texto.Length == 0) continue;
			PanelContainer pilula = Pilula(texto, cor);
			pilula.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
			topo.AddChild(pilula);
		}
		bloco.AddChild(topo);
		LinhaDoProgresso(e.Tipo, e.Progresso, bloco);

		var quando = new List<string>(2);
		if (e.Tipo == TipoDeEnsinamento.Forma && e.Tentativas > 0 && !(e.Assistida && e.Tentativas == 1))
			quando.Add($"{quemTentou} {e.Tentativas} {(e.Tentativas == 1 ? "vez" : "vezes")}");
		if (e.HaSegundos > 0)
			quando.Add((e.Tipo == TipoDeEnsinamento.Forma ? "a primeira " : "ensinada ") + HaQuantoTempo(e.HaSegundos + passou));
		if (quando.Count > 0) Nota(string.Join("  ·  ", quando), bloco).SetMeta("discipulado", "quando");
		pai.AddChild(bloco);
	}

	private static HFlowContainer FilaDeBotoes(Control pai)
	{
		var f = new HFlowContainer();
		f.AddThemeConstantOverride("h_separation", 6);
		f.AddThemeConstantOverride("v_separation", 4);
		pai.AddChild(f);
		return f;
	}

	// =====================================================================
	// O GESTO SEM VOLTA PEDE DOIS CLIQUES
	// =====================================================================
	/// <summary>Qual gesto esta armado (a chave do botao) e ate quando o segundo clique vale. Ver <see cref="BotaoDeConfirmar"/>.</summary>
	private string _gestoArmado = "";
	private ulong _gestoArmadoAteMs;

	private bool GestoArmado(string chave) => _gestoArmado == chave && Time.GetTicksMsec() < _gestoArmadoAteMs;

	/// <summary>
	/// UM BOTAO DE DOIS CLIQUES. Dispensar um aluno e deixar o mestre desfazem o vinculo e apagam o diario dele, e o
	/// botao mora num cartao que se lê rolando: o primeiro clique ARMA (o texto vira a confirmacao, com o nome), o
	/// segundo -- dentro de <see cref="SegundosPraConfirmar"/> s -- faz. O metadado `acao` diz qual gesto e, e o
	/// `armado` em qual dos dois cliques o botao esta.
	/// </summary>
	private Button BotaoDeConfirmar(string rotulo, string confirmacao, string chave, string acao, string dica, Action gesto)
	{
		bool armado = GestoArmado(chave);
		var b = new Button
		{
			Text = armado ? confirmacao : rotulo, TooltipText = dica, FocusMode = Control.FocusModeEnum.None,
		};
		b.SetMeta("acao", acao);
		b.SetMeta("armado", armado);
		b.AddThemeFontSizeOverride("font_size", 12);
		if (armado)
		{
			b.AddThemeColorOverride("font_color", Tema.Perigo);
			b.AddThemeColorOverride("font_hover_color", Tema.Perigo);
			b.AddThemeStyleboxOverride("normal", Tema.Caixa(new Color(Tema.Perigo, 0.16f), Tema.Perigo, 8));
		}
		b.Pressed += () =>
		{
			if (GestoArmado(chave))
			{
				_gestoArmado = "";
				gesto();
			}
			else
			{
				_gestoArmado = chave;
				_gestoArmadoAteMs = Time.GetTicksMsec() + (ulong)(SegundosPraConfirmar * 1000);
			}
			Redesenhar();
		};
		return b;
	}

	// =====================================================================
	// A ASSINATURA
	// =====================================================================
	/// <summary>
	/// A ASSINATURA DE CACHE DESTA ABA (ver `MenuJogo.Assinatura`): TUDO o que a pagina desenha -- o conteudo do painel,
	/// os "ha quanto tempo" como TEXTO (a pagina se refaz quando a frase muda, e nao a cada segundo), a leitura de
	/// forca de cada cartao, se ha retrato guardado, e o gesto armado. A busca entra porque ela troca a pagina toda.
	/// Nunca vazia (vazia quer dizer "sem cache", e os botoes morreriam debaixo do mouse a cada pacote de ficha).
	/// </summary>
	private string ExtraDaAssinaturaDeAlunos(SheetState f)
	{
		var sb = new StringBuilder("discipulado|").Append(_busca.Text.Trim());
		if (GameClient.Instance is not { } c) return sb.ToString();
		if (c.MestreEAlunos is not { } p) return sb.Append("|buscando").ToString();

		uint passou = SegundosDesdeQueOPainelChegou(c);
		bool sense = _atributos.Tem(Protocol.Poder.Sense), scouter = _atributos.Tem(Protocol.Poder.Scouter) || c.SentidosSaoDoScouter;
		sb.Append('|').Append(ProgressoDeAluno.Conteudo(p)).Append(sense ? "|S" : "|-").Append(scouter ? 'C' : '-');

		void Pessoa(PessoaNoPainel q, string papel)
		{
			sb.Append('|').Append(QuemEDesdeQuando(q, passou, papel))
			  .Append(',').Append(ForcaSentida(c, q) is { } r ? r.ToString("0") : CasoSemLeitura(q.Online, sense, scouter))
			  .Append(VistosDeGente.De(q.Nome) != null ? ",f" : ",-");
		}
		void Quando(EnsinamentoNoPainel e) => sb.Append(e.HaSegundos > 0 ? HaQuantoTempo(e.HaSegundos + passou) : "-").Append(';');

		if (p.Mestre is { } m)
		{
			Pessoa(m.Quem, "m");
			foreach (EnsinamentoDoMestre e in m.Ensinamentos) Quando(e.Meu);
			foreach (string colega in m.Colegas) sb.Append(VistosDeGente.De(colega) != null ? 'f' : '-');
		}
		foreach (AlunoNoPainel a in p.Alunos)
		{
			Pessoa(a.Quem, "a");
			foreach (EnsinamentoNoPainel e in a.Ensinamentos) Quando(e);
		}
		sb.Append("||").Append(Time.GetTicksMsec() < _gestoArmadoAteMs ? _gestoArmado : "");

		// A LISTA DO "ENSINAR...": de quem esta aberta e, aberta, o que ela lista (o que eu sei e o que inventei).
		sb.Append("||e:").Append(_ensinandoA);
		if (_ensinandoA.Length > 0)
		{
			sb.Append(':').Append(c.SkillsAprendidas.Count);
			foreach (TecnicaCustomizada t in c.Customizadas)
				if (t.Criada && !t.Ensinada) sb.Append(',').Append(t.Id).Append(t.Nome);
		}
		return sb.ToString();
	}
}
