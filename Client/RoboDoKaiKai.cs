using Godot;
using Jandirus.Core.World;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA TELA DO KAI KAI (<c>--diagkaikai</c>, 2026-10-23). Fotografa a tela aberta pelo botao do verbo, o clique (o corpo piscando
/// antes de sumir), a chegada em Namek com o clarao e a carona, o Kaioshin forjado chegando e indo embora visto por quem esta perto, e o
/// salto pro mundo gerado do livro de batismo -- que o servidor ainda nao tinha gerado. Cada regra que se ve tem o contra-exemplo dela
/// (a tela que nao abre, a linha sem arte, o botao sem destino, o corpo que some no tique do clarao, o clarao da chegada antes do corpo).
///
///     Godot --path . --host --rede 10831 --campoteste 12 --horateste 0.5 --semfauna --diagkaikai --raca Human --conta bancada_kaikai --nome Kaikai
///     (com --janela, pras fotos)
///
/// O BOTAO DO VERBO ESTA NA ABA OTHER (2026-10-24, dono: "Mover pra aba Other"). Ate a r1 a categoria dele ("Outros", a de
/// `Tecnicas.Portadas`) nao era aba nenhuma e so a busca o achava; agora o robo o aperta na pagina da Other (K0 fotografa a aba e a
/// tela de teclas, e o contra-exemplo e a categoria de antes). E a chegada dos doze de sempre e no BERCO do planeta (dono: "Usar o
/// berco do planeta"): K3c e K4c conferem o chao de Namek, e K4d e o ponto fixo de antes, na agua.
/// </summary>
public partial class RoboDoKaiKai : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private int _ok;

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>Quantos quadros o corpo tem de ser VISTO piscando pra a saida contar como vista (o salto espera 0,2 s pra sumir).</summary>
	private const int QuadrosQueContamComoVisto = 3;

	public override void _Ready() => _ = Rodar();

	public override void _ExitTree() => DesligarOsDefeitos();

	/// <summary>A chave do botao do verbo (a que o `Habilidades.DasSkills` escreve).</summary>
	private const string ChaveDoVerbo = "hab:Kai_Kai";

	private static void DesligarOsDefeitos()
	{
		TelaDoKaiKai.NaoAbreDeTeste = TelaDoKaiKai.BotaoSemDestinoDeTeste = TelaDoKaiKai.SemArteDeTeste = false;
		Jandirus.Server.GameServer.KaiKaiSomeNaHoraDeTeste = Jandirus.Server.GameServer.KaiKaiClaraoNaHoraDeTeste = false;
		Jandirus.Server.GameServer.ChegadaNoPontoFixoDeTeste = false;
		Habilidades.OutrosForaDaAbaDeTeste = false;
	}

	private async Tarefa Rodar()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null
									  && MenuJogo.Instancia != null && TelaDoKaiKai.Instancia != null, 20);
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu || TelaDoKaiKai.Instancia is not { } tela)
		{
			Conferir(false, "(montagem) o host entrou, com mundo, servidor, menu e a tela do Kai Kai");
			Fechar();
			return;
		}
		try
		{
			Conferir(srv.PalcoKK_Montar(cli.LocalId), "(montagem) o palco deu o Kai Kai, o Ki cheio, a carona colada e o mundo do livro");
			Conferir(await Ate(() => Verbos.PorChave(ChaveDoVerbo) != null, 8), "(montagem) o botao do verbo Kai Kai chegou ao cliente");
			await Quadros(10);

			await OBotaoNaAbaOther(menu);
			await ATelaPeloBotao(srv, menu, tela);
			await OCliqueEAChegada(cli, srv, tela);
			await QuemEstaPertoVe(cli, srv);
			await OMundoDoLivro(cli, srv, tela);
		}
		catch (Exception e)
		{
			Conferir(false, "(montagem) o robo rodou inteiro: " + e.Message);
		}
		finally
		{
			DesligarOsDefeitos();
			srv.PalcoKK_Limpar(cli.LocalId);
		}
		Fechar();
	}

	// =====================================================================
	// K0. O BOTAO NA ABA OTHER -- E NA TELA DE TECLAS
	// =====================================================================
	/// <summary>
	/// (2026-10-24, dono: "Mover pra aba Other") A categoria "Outros" do catalogo virou a aba Other (`Habilidades.AbaDoBotao`): o botao
	/// do Kai Kai mora la, no cartao "Diversos", com a frase do que faz, e a tela de teclas o oferece pra ligar. O contra-exemplo e a
	/// categoria de antes: o botao fica so na busca.
	/// </summary>
	private async Tarefa OBotaoNaAbaOther(MenuJogo menu)
	{
		Conferir(Verbos.Da(Verbos.Outros).Any(v => v.Chave == ChaveDoVerbo),
				 "K0. o verbo Kai Kai esta na categoria da aba Other (a 'Outros' do catalogo agora e a Other)");
		Button? b = await BotaoDoVerboNaAbaOther(menu);
		string frase = Verbos.PorChave(ChaveDoVerbo)?.Descricao ?? "";
		bool comFrase = frase.Length > 0 && menu.PaginaDeTeste(Verbos.Outros) is { } pg
						&& Todos(pg).OfType<Label>().Any(l => l.IsVisibleInTree() && !l.IsQueuedForDeletion() && l.Text == frase);
		Conferir(b != null && CartaoDe(b) == "Diversos" && comFrase,
				 $"K0b. ...e o botao 'Kai Kai' esta na pagina da Other, no cartao 'Diversos', com a frase do que faz embaixo [cartao '{(b == null ? "-" : CartaoDe(b))}', frase {comFrase}]");
		Fotografar("user://kaikai-0-aba-other.png");
		menu.Fechar();
		await Quadros(2);

		bool naTela = await NaTelaDeTeclas("Kai Kai", "user://kaikai-0b-tela-de-teclas.png");
		Conferir(naTela, "K0c. ...e a tela de teclas o oferece pra ligar (na secao 'Outros', a da aba Other)");

		// O CONTRA-EXEMPLO: a categoria de antes, fora de toda aba (na r1 so a busca o achava)
		Habilidades.OutrosForaDaAbaDeTeste = true;
		try
		{
			RegistrarDeNovo();
			await Quadros(2);
			Button? semAba = await BotaoDoVerboNaAbaOther(menu);
			menu.Fechar();
			await Quadros(2);
			bool naTelaComDefeito = await NaTelaDeTeclas("Kai Kai", null);
			Conferir(!Verbos.Da(Verbos.Outros).Any(v => v.Chave == ChaveDoVerbo) && semAba == null && !naTelaComDefeito
					 && Verbos.Buscar("Kai Kai").Any(v => v.Chave == ChaveDoVerbo),
					 "K0d. (defeito injetado: a categoria 'Outros' fora de toda aba) as reguas de K0, K0b e K0c REPROVAM -- o botao so fica na busca");
		}
		finally
		{
			Habilidades.OutrosForaDaAbaDeTeste = false;
			RegistrarDeNovo();
		}
		await Quadros(2);
		Conferir(Verbos.Da(Verbos.Outros).Any(v => v.Chave == ChaveDoVerbo), "(volta) sem o defeito o Kai Kai volta pra aba Other");
	}

	// =====================================================================
	// K1. A TELA, ABERTA PELO BOTAO DO VERBO
	// =====================================================================
	private async Tarefa ATelaPeloBotao(Jandirus.Server.GameServer srv, MenuJogo menu, TelaDoKaiKai tela)
	{
		Button? verbo = await BotaoDoVerboNaAbaOther(menu);
		Conferir(verbo != null, "(montagem) a aba Other do menu P tem o botao do verbo Kai Kai");
		if (verbo == null) return;
		verbo.EmitSignal(BaseButton.SignalName.Pressed);
		bool abriu = await Ate(() => tela.NaTela, 4);
		await Quadros(4);
		Conferir(abriu && !MenuJogo.NaFrenteDoJogo,
				 $"K1. o botao do verbo ABRE a tela do Kai Kai (a lista veio do servidor) e o menu P sai da frente [na tela {tela.NaTela}]");

		string chave = srv.PalcoKK_ChaveDoGerado();
		int doze = tela.Destinos.Count(d => !d.Gerado);
		Conferir(doze == 12 && tela.NomeDe(chave) == Jandirus.Server.GameServer.BatismoDoPalcoKK && tela.NomeDe("Earth") == "Terra",
				 $"K1b. ...com os doze de sempre e o mundo do livro pelo nome do batismo [{doze} fixos; {chave} = '{tela.NomeDe(chave)}']");
		Conferir(tela.Destinos.Count > 12 && tela.Destinos.All(d => tela.ArteDe(d.Chave) != null)
				 && tela.BotaoDe("Earth") == null && tela.BotaoDe("Namek") != null && tela.BotaoDe(chave) != null,
				 "K1c. ...cada linha com a arte do planeta e o botao Teletransportar -- menos a Terra, onde ele esta");
		Fotografar("user://kaikai-1-tela.png");

		// O CONTRA-EXEMPLO DA ABERTURA: a lista chega (o contador sobe) e a tela nao abre
		tela.Fechar();
		await Quadros(2);
		int antes = tela.Recebidas;
		TelaDoKaiKai.NaoAbreDeTeste = true;
		try
		{
			ApertarOVerbo();
			bool chegou = await Ate(() => tela.Recebidas > antes, 3);
			await Quadros(6);
			Conferir(chegou && !tela.NaTela,
					 "K1d. (defeito injetado: a lista chega e a tela nao abre) a regua de K1 REPROVA -- a lista chegou e nada abriu");
		}
		finally { TelaDoKaiKai.NaoAbreDeTeste = false; }

		// O CONTRA-EXEMPLO DA ARTE
		TelaDoKaiKai.SemArteDeTeste = true;
		try
		{
			ApertarOVerbo();
			await Ate(() => tela.NaTela, 3);
			await Quadros(2);
			Conferir(tela.NaTela && tela.Destinos.Any(d => tela.ArteDe(d.Chave) == null),
					 "K1e. (defeito injetado: as linhas sem a arte do planeta) a regua de K1c REPROVA");
		}
		finally
		{
			TelaDoKaiKai.SemArteDeTeste = false;
			tela.Fechar();
		}
		await Quadros(2);
	}

	// =====================================================================
	// K2/K3. O CLIQUE, A SAIDA E A CHEGADA
	// =====================================================================
	private async Tarefa OCliqueEAChegada(GameClient cli, Jandirus.Server.GameServer srv, TelaDoKaiKai tela)
	{
		// O CONTRA-EXEMPLO PRIMEIRO (o salto de verdade tira o corpo da Terra): o botao sem o destino nao leva ninguem
		srv.PalcoKK_Encher(cli.LocalId);
		ApertarOVerbo();
		await Ate(() => tela.NaTela, 3);
		TelaDoKaiKai.BotaoSemDestinoDeTeste = true;
		try
		{
			int antes = tela.Recebidas;
			tela.BotaoDe("Namek")?.EmitSignal(BaseButton.SignalName.Pressed);
			await Ate(() => tela.Recebidas > antes, 3);   // o servidor so manda a lista de novo
			await Quadros(10);
			Conferir(cli.Zone.Name == "Earth" && tela.UltimoPedido == "Kai_Kai",
					 $"K2a. (defeito injetado: o botao manda o verbo sem o destino) a regua de K2 REPROVA -- ninguem viaja [{cli.Zone.Name}; '{tela.UltimoPedido}']");
		}
		finally { TelaDoKaiKai.BotaoSemDestinoDeTeste = false; }
		tela.Fechar();
		await Quadros(4);

		// O CLIQUE DE VERDADE
		srv.PalcoKK_Encher(cli.LocalId);
		ApertarOVerbo();
		await Ate(() => tela.NaTela, 3);
		Button? namek = tela.BotaoDe("Namek");
		Conferir(namek != null, "(montagem) a linha de Namek tem o botao Teletransportar");
		namek?.EmitSignal(BaseButton.SignalName.Pressed);
		Conferir(tela.UltimoPedido == "Kai_Kai:Namek" && !tela.NaTela, $"K2. o clique manda Kai_Kai:Namek e a tela fecha ['{tela.UltimoPedido}']");

		int naTerra = await PiscarEFotografar(cli.LocalId, () => cli.Zone.Name == "Earth", "user://kaikai-2-saida.png", 2);
		Conferir(naTerra >= 1, $"K2b. ...e o corpo PISCA ainda na Terra, antes de sumir (o efeito de desviar um golpe, com o som do Instant Transmission) [{naTerra} quadro(s)]");

		bool chegou = await Ate(() => cli.Zone.Name == "Namek", 6);
		int laEmNamek = chegou ? await PiscarEFotografar(cli.LocalId, () => cli.Zone.Name == "Namek", "user://kaikai-3-chegada.png", 2) : 0;
		Conferir(chegou && laEmNamek >= 1, $"K3. ...e CHEGA em Namek piscando [zona {cli.Zone.Name}; {laEmNamek} quadro(s)]");
		float carona = srv.PalcoKK_Distancia(cli.LocalId, "Carona");
		Conferir(carona >= 0 && carona <= 2 * ZoneCollision.TileSize, $"K3b. ...com a carona que estava colada do lado [{carona:0} px]");
		bool noBerco = srv.PalcoKK_NoBerco(cli.LocalId, out string ondeCaiu);
		Conferir(noBerco, $"K3c. ...no BERCO de Namek, em chao firme -- e nao na agua do ponto fixo de antes [{ondeCaiu}]");
		await Ate(() => !Piscando(cli.LocalId), 2);

		// A FOTO DO CHAO: a tela de carregamento cobre a chegada (a foto 3); esta e a de depois dela, com Namek desenhada em volta.
		await Ate(() => TelaDeCarregamento.Instancia is not { NoAr: true }, 10);
		await Quadros(30);
		Fotografar("user://kaikai-3b-namek-no-chao.png");
	}

	// =====================================================================
	// K4/K5. QUEM ESTA PERTO VE O CLARAO -- o Kaioshin do palco chegando e indo embora
	// =====================================================================
	private async Tarefa QuemEstaPertoVe(GameClient cli, Jandirus.Server.GameServer srv)
	{
		// quem joga sai do ponto de chegada: o Kaioshin cai nele
		srv.PalcoKK_DePasso(cli.LocalId, 3);
		await Quadros(20);

		int k = srv.PalcoKK_KaioshinSalta("Namek");
		int chegada = await PiscarEFotografar(k, () => true, "user://kaikai-4-chegada-vista.png", 5);
		Conferir(chegada >= 1, $"K4. quem esta em Namek VE o Kaioshin chegar PISCANDO (o clarao sai depois de o corpo entrar no retrato da zona) [{chegada} quadro(s)]");
		bool kNoBerco = srv.PalcoKK_NoBerco(k, out string kOnde);
		Conferir(kNoBerco, $"K4c. ...e ele pousa no berco de Namek, em chao, perto de quem esta la [{kOnde}]");
		await Ate(() => !Piscando(k), 2);
		await Quadros(15);

		srv.PalcoKK_KaioshinSalta("Earth");
		int saida = await PiscarEFotografar(k, () => true, "user://kaikai-5-saida-vista.png", 4);
		bool sumiu = await Ate(() => !Existe(k), 3);
		Conferir(sumiu && saida >= QuadrosQueContamComoVisto,
				 $"K5. ...e o VE piscar antes de sumir quando ele vai embora (o corpo sai 0,2 s depois do clarao) [{saida} quadro(s) piscando]");

		// O CONTRA-EXEMPLO DA SAIDA: o corpo some no tique do clarao, e quem fica nao ve clarao nenhum. Antes, ele volta -- e o clarao
		// da chegada dele tem de ACABAR (piscar e parar), senao os quadros dele contariam como os da saida.
		srv.PalcoKK_KaioshinSalta("Namek");
		await Ate(() => Existe(k), 5);
		await Ate(() => Piscando(k), 1.5);
		await Ate(() => !Piscando(k), 2);
		await Quadros(20);
		Jandirus.Server.GameServer.KaiKaiSomeNaHoraDeTeste = true;
		try
		{
			int quadros = 0;
			srv.PalcoKK_KaioshinSalta("Earth");
			bool foi = await Ate(() => { if (Piscando(k)) quadros++; return !Existe(k); }, 4);
			Conferir(foi && quadros < QuadrosQueContamComoVisto,
					 $"K5b. (defeito injetado: o corpo some no tique do clarao) a regua de K5 REPROVA -- quem fica nao ve o clarao [{quadros} quadro(s)]");
		}
		finally { Jandirus.Server.GameServer.KaiKaiSomeNaHoraDeTeste = false; }
		await Quadros(15);

		// O CONTRA-EXEMPLO DA CHEGADA: o clarao sai no tique do salto, antes de o corpo existir na tela de quem esta la
		Jandirus.Server.GameServer.KaiKaiClaraoNaHoraDeTeste = true;
		try
		{
			bool viu = false;
			srv.PalcoKK_KaioshinSalta("Namek");
			bool apareceu = await Ate(() => { if (Piscando(k)) viu = true; return Existe(k); }, 5);
			await Ate(() => { if (Piscando(k)) viu = true; return false; }, 0.8);
			Conferir(apareceu && !viu, "K4b. (defeito injetado: o clarao da chegada no tique do salto) a regua de K4 REPROVA -- o corpo chega sem piscar");
		}
		finally { Jandirus.Server.GameServer.KaiKaiClaraoNaHoraDeTeste = false; }

		// O CONTRA-EXEMPLO DA CHEGADA NO BERCO: o salto volta a pousar no ponto fixo de antes (o `SpawnPos`), e o Kaioshin cai na agua.
		// Ele vai pra Terra antes (o Kai Kai recusa o planeta em que ja se esta), e o ponto e escolhido no aperto, com o defeito ligado.
		await Ate(() => !Piscando(k), 2);
		await Quadros(15);
		srv.PalcoKK_KaioshinSalta("Earth");
		await Ate(() => srv.PalcoKK_ZonaDoKaioshin() == "Earth", 4);
		await Quadros(15);
		Jandirus.Server.GameServer.ChegadaNoPontoFixoDeTeste = true;
		try
		{
			srv.PalcoKK_KaioshinSalta("Namek");
			bool la = await Ate(() => srv.PalcoKK_ZonaDoKaioshin() == "Namek", 4);
			bool noBercoComDefeito = srv.PalcoKK_NoBerco(k, out string ondeComDefeito);
			Conferir(la && !noBercoComDefeito && ondeComDefeito.Contains("AGUA"),
					 $"K4d. (defeito injetado: o salto pousa no ponto fixo de antes) a regua de K4c REPROVA -- o Kaioshin cai na agua de Namek [{ondeComDefeito}]");
		}
		finally { Jandirus.Server.GameServer.ChegadaNoPontoFixoDeTeste = false; }
	}

	// =====================================================================
	// K6. O MUNDO DO LIVRO, QUE O SERVIDOR AINDA NAO TINHA
	// =====================================================================
	private async Tarefa OMundoDoLivro(GameClient cli, Jandirus.Server.GameServer srv, TelaDoKaiKai tela)
	{
		srv.PalcoKK_Encher(cli.LocalId);
		ApertarOVerbo();
		bool abriu = await Ate(() => tela.NaTela, 3);
		string chave = srv.PalcoKK_ChaveDoGerado();
		Button? b = tela.BotaoDe(chave);
		Conferir(abriu && b != null, $"(montagem) de Namek a tela abre, e o mundo do livro tem o botao [{chave}]");
		b?.EmitSignal(BaseButton.SignalName.Pressed);

		ZoneKey destino = srv.PalcoKK_ZonaDoGerado();
		bool chegou = await Ate(() => cli.Zone.Equals(destino), 20);
		Conferir(chegou, $"K6. o clique no mundo do livro -- que o servidor ainda nao tinha gerado -- espera ele nascer e LEVA ate la [{cli.Zone}]");
		int piscou = chegou ? await PiscarEFotografar(cli.LocalId, () => cli.Zone.Equals(destino), "user://kaikai-6-mundo-do-livro.png", 3) : 0;
		Conferir(piscou >= 1 && srv.PalcoKK_NaChegadaDoGerado(cli.LocalId),
				 $"K6b. ...chegando piscando, no ponto que o gerador garantiu livre (a chegada do mundo) [{piscou} quadro(s)]");
		await Quadros(40);
		Fotografar("user://kaikai-7-mundo-do-livro-depois.png");
	}

	// =====================================================================
	// AS PORTAS
	// =====================================================================
	/// <summary>O verbo, pelo mesmo caminho da tecla ligada (`Atalhos.Disparar`): o `Acionar` do botao.</summary>
	private static void ApertarOVerbo() => Verbos.PorChave(ChaveDoVerbo)?.Acionar?.Invoke();

	/// <summary>
	/// Abre o menu P na aba Other e devolve o botao "Kai Kai" da pagina dela, rolado ate ficar a vista (nulo = a pagina nao o tem). O
	/// texto e o EXATO: o resultado da busca seria "Kai Kai [Other]", e este e o botao da aba.
	/// </summary>
	private async System.Threading.Tasks.Task<Button?> BotaoDoVerboNaAbaOther(MenuJogo menu)
	{
		if (!menu.Visible) menu.Abrir();
		menu.IrPara(Verbos.Outros);
		await Quadros(3);
		Button? achado = null;
		await Ate(() => menu.PaginaDeTeste(Verbos.Outros) is { } pg
						&& (achado = Todos(pg).OfType<Button>()
								  .FirstOrDefault(b => b.IsVisibleInTree() && !b.IsQueuedForDeletion() && b.Text == "Kai Kai")) != null, 2);
		if (achado == null) return null;
		for (Node? p = achado.GetParent(); p != null; p = p.GetParent())
			if (p is ScrollContainer rolagem) { rolagem.EnsureControlVisible(achado); break; }
		await Quadros(3);
		return achado;
	}

	/// <summary>O tema do cartao da aba Other em que este botao mora (o metadado `grupo`); "" = solto na pagina.</summary>
	private static string CartaoDe(Node n)
	{
		for (Node? p = n.GetParent(); p != null; p = p.GetParent())
			if (p is PanelContainer c && c.HasMeta("grupo")) return c.GetMeta("grupo").AsString();
		return "";
	}

	/// <summary>
	/// A TELA DE TECLAS (a do menu de pausa) tem a linha deste verbo pra ligar? Abre, procura o nome na busca dela, olha, fotografa se
	/// pedido, limpa a busca e fecha. A lista dela sai das quatro abas de verbo -- e por isso que a "Outros" de antes nao aparecia la.
	/// </summary>
	private async System.Threading.Tasks.Task<bool> NaTelaDeTeclas(string nome, string? foto)
	{
		if (TelaDeTeclas.Instancia is not { } teclas) return false;
		teclas.Abrir();
		await Quadros(2);
		LineEdit? busca = Todos(teclas).OfType<LineEdit>().FirstOrDefault(l => l.PlaceholderText.StartsWith("procurar", StringComparison.Ordinal));
		if (busca != null)
		{
			busca.Text = nome;
			busca.EmitSignal(LineEdit.SignalName.TextChanged, nome);
		}
		await Quadros(4);
		bool tem = Todos(teclas).OfType<Label>().Any(l => l.IsVisibleInTree() && !l.IsQueuedForDeletion() && l.Text == nome);
		if (foto != null) Fotografar(foto);
		if (busca != null)
		{
			busca.Text = "";
			busca.EmitSignal(LineEdit.SignalName.TextChanged, "");
		}
		teclas.Fechar();
		await Quadros(2);
		return tem;
	}

	/// <summary>O botao do Kai Kai nasce de novo do livro (`Habilidades.DasSkills`): o registro antigo sai antes, senao o nome repetido o barra.</summary>
	private static void RegistrarDeNovo()
	{
		Verbos.Esquecer("Kai Kai");
		Habilidades.DasSkills();
	}

	/// <summary>
	/// ESPERA O CORPO PISCAR (onde vale) e fotografa no SEGUNDO quadro com as listras -- a textura da tela e a do quadro ja desenhado, e
	/// no primeiro as listras acabaram de nascer. Devolve quantos quadros ele foi visto piscando; para quando o clarao acaba ou o corpo
	/// some. Sem clarao, a foto sai mesmo assim (o relatorio mostra o que havia na tela).
	/// </summary>
	private async System.Threading.Tasks.Task<int> PiscarEFotografar(int id, Func<bool> ondeVale, string foto, double segundos)
	{
		int quadros = 0;
		bool fotografou = false;
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (Piscando(id) && ondeVale())
			{
				quadros++;
				if (quadros == 2) { Fotografar(foto); fotografou = true; }
			}
			else if (quadros > 0) break;
			await Quadros(1);
		}
		if (!fotografou) Fotografar(foto);
		return quadros;
	}

	private static bool Existe(int id) => World.Instancia?.CorpoDeTeste(id) != null;

	private static bool Piscando(int id) =>
		World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<EsquivaZanzoken>(EsquivaZanzoken.NomeDoNode) != null;

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private void Conferir(bool ok, string oque)
	{
		if (ok) { _ok++; _linhas.Add("  OK    " + oque); return; }
		_falhas.Add(oque);
		_linhas.Add("  FALHA " + oque);
	}

	private void Fotografar(string destino)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath(destino);
		img.SavePng(caminho);
		_linhas.Add("  --     foto salva em " + caminho);
	}

	private async Tarefa Quadros(int n)
	{
		for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[diagkaikai] " + l);
		GD.Print($"[diagkaikai] PLACAR: {_ok} OK, {_falhas.Count} FALHA(S)");
		GD.Print(_falhas.Count == 0
			? "[diagkaikai] ===== TUDO OK ====="
			: $"[diagkaikai] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[diagkaikai]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
