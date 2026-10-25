using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA TROPA DO FREEZA (`--diagtropa`, 2026-10-18). O pedido do dono, em resumo: o admin poder pular os dias de aviso do chefe de saga
/// (ele nasce agora); e, com o Freeza em Namek, muitos soldados NPC de raca alienigena pelo planeta, atacando os Namekuseijins e pegando as
/// Esferas do Dragao do chao -- que so voltam derrotando quem as pegou --, de armadura Saiyajin / do exercito do Freeza. Com janela e
/// `--host` (o host e admin), nascendo Humano no campo da Terra:
///   R1. a aba Admin, o cartao Sagas, "Ver as sagas": cada saga com o "Chegar agora" ao lado do "Resetar" -- foto `tropa-1-admin-chegar-agora`;
///   R2. o "Chegar agora" da saga do Freeza de Namek: ele chega na hora, com a cena da chegada pra todos -- foto `tropa-2-freeza-chegou`;
///   R3. Namek: a tropa desembarcou (o desembarque do `npcs.json`: vinte de uma vez desde 2026-10-24), e tres soldados vem pra perto -- de
///       armadura -- foto `tropa-3-soldados-de-armadura`;
///   R3b. (2026-10-24) os soldados na tela vestem as seis folhas que o dono escolheu -- lidas no BONECO de cada um, no cliente;
///   R4. um Namekuseijin na frente de um soldado: o soldado o ataca -- foto `tropa-4-ataca-namekuseijin`; CONTRA-EXEMPLO
///       (`SoldadoComoHostilComumDeTeste`, o "AI nao ataca AI" do hostil comum): o Namekuseijin sai sem um arranhao (e o soldado vai pra cima
///       do fotografo, o unico jogador a vista) -- foto `tropa-4b-defeito-hostil-comum`;
///   R5. uma esfera do chao na frente de um soldado: ele a pega, e o chat diz quem pegou -- foto `tropa-5-pegou-a-esfera`;
///   R6. o soldado nocauteado: a esfera cai onde ele esta, e o chat diz -- foto `tropa-6-esfera-caiu`.
/// O Freeza fica nocauteado do outro lado de Namek durante as fotos (ver `GameServer.TropaPalco.cs`).
///
///     Godot --path . --host --rede 8561 --campoteste 12 --horateste 0.5 --semfauna --diagtropa --raca Human --conta bancada_tropa --nome Fotografo
/// </summary>
public partial class RoboDaTropa : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _ditas = [];

	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private void Conferir(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok    " : "  FALHA ") + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Ready() => _ = Rodar();

	private async Tarefa Rodar()
	{
		try { await Percurso(); }
		catch (Exception e) { Conferir(false, $"o roteiro correu ate o fim sem excecao ({e.GetType().Name}: {e.Message})"); GD.Print(e.ToString()); }
		S?.PalcoT_Limpar();
		Fechar();
	}

	private void Ouvir(Protocol.Fala canal, string quem, string texto) => _ditas.Add(texto);

	private bool Disse(string trecho) => _ditas.Exists(t => t.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private string UltimasDitas() => string.Join(" | ", _ditas.TakeLast(3));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null && MenuJogo.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo e o menu P nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuJogo.Instancia is not { } menu) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dtropa] ===== O ADMIN QUE PULA OS DIAS E A TROPA DO FREEZA EM NAMEK (dono, 2026-10-18) =====");
		srv.EmbateDeFoto_CeuLimpo(eu);
		cli.Falou += Ouvir;
		try
		{
			// ---------------------------------------------------------- R1. o cartao Sagas
			bool admin = cli.Atributos.Tem(Protocol.Poder.Admin);
			await Aba(menu, "Admin");
			Button? ver = Todos(menu).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.Text == "Ver as sagas");
			ver?.EmitSignal(BaseButton.SignalName.Pressed);
			bool listou = await Ate(() => BotaoDaSaga(menu, "agora_saga", "freeza_namek") != null, 8);
			await MostrarOCartaoDasSagas(menu);
			int comOBotao = Todos(menu).OfType<Button>().Count(b => b.IsVisibleInTree() && b.HasMeta("agora_saga"));
			Fotografar("user://tropa-1-admin-chegar-agora.png");
			Conferir(admin && listou && comOBotao >= 4,
					 "R1. O CARTAO SAGAS DA ABA ADMIN TEM O \"CHEGAR AGORA\" em cada saga, ao lado do Resetar (o pedido do dono: o admin pular os dias "
					 + $"de aviso) [admin {admin}; listou {listou}; botoes {comOBotao}]");

			// ---------------------------------------------------------- R2. o Freeza chega na hora
			BotaoDaSaga(menu, "agora_saga", "freeza_namek")?.EmitSignal(BaseButton.SignalName.Pressed);
			// O MENU FECHA NA HORA: a cena da chegada se desenha sobre o mundo, e nao sobre a lista que ainda diz "adormecida" (ela so volta com o
			// estado novo um instante depois -- a primeira rodada fotografou as duas por cima uma da outra).
			menu.Fechar();
			bool chegou = await Ate(() => srv.PalcoT_FreezaEmNamek(), 8);
			await Segundos(2.5);
			Fotografar("user://tropa-2-freeza-chegou.png");
			Conferir(chegou && Disse("chegando agora"),
					 $"R2. \"CHEGAR AGORA\" NA SAGA DO FREEZA DE NAMEK: ele chega na hora, com a cena da chegada [no mundo {chegou}; ouvi: {UltimasDitas()}]");
			await Segundos(Jandirus.Client.CenaDaChegada.Duracao);

			// ---------------------------------------------------------- R3. Namek, e a tropa de armadura
			ZoneKey namek = ZoneKey.Premade("Namek");
			bool foi = srv.PalcoT_LevarANamek(eu) && await Ate(() => NaTela(cli, namek), 25);
			srv.EmbateDeFoto_CeuLimpo(eu);   // o ceu de Namek tambem: a primeira rodada saiu escura, na chuva de Namek
			await Segundos(1);
			bool desembarcou = await Ate(() => srv.PalcoT_Tropa().DePe >= 6, 8);
			int vieram = srv.PalcoT_SoldadosEmVolta(eu);
			await Segundos(0.4);
			Fotografar("user://tropa-3-soldados-de-armadura.png");
			(int dePe, int deArmadura) = srv.PalcoT_Tropa();
			Conferir(foi && desembarcou && vieram == 3 && dePe > 0 && deArmadura == dePe,
					 "R3. EM NAMEK, A TROPA DESEMBARCOU, e tres soldados vem pra perto -- todos de armadura do exercito "
					 + $"[em Namek {foi}; de pe {dePe}; de armadura {deArmadura}; vieram {vieram}]");

			// ---------------------------------------------------------- R3b. as seis folhas, vistas no cliente (2026-10-24)
			// O dono: *"os aliens soldados dele deveriam usar esses sprites base [...] sendo aleatorio o sprite escolhido ao spawnar cada npcs
			// soldado"*. A regua le a folha que o BONECO de cada soldado na tela veste: o indice da faixa reservada atravessou o fio, e o
			// `VisualCatalog.CorpoSprite` do cliente o devolveu como folha.
			string[] seis = Jandirus.Core.Npc.FolhasDaTropa.Folhas.Select(f => System.IO.Path.GetFileNameWithoutExtension(f)).ToArray();
			List<string> naTela = srv.PalcoT_IdsDaTropa().Select(Folha).Where(f => f.Length > 0).ToList();
			Conferir(naTela.Count >= 3 && naTela.TrueForAll(f => seis.Contains(f)),
					 "R3b. OS SOLDADOS NA TELA VESTEM AS FOLHAS QUE O DONO ESCOLHEU (Alien 9, Alien 4, Alien 8, Alien 1, Race Kui e CavemanAlien) "
					 + $"[{naTela.Count} na tela: {string.Join(", ", naTela.GroupBy(f => f).OrderBy(g => g.Key).Select(g => $"{g.Key} x{g.Count()}"))}]");

			// ---------------------------------------------------------- R4. o soldado ataca o Namekuseijin
			(int nk, int soldado) = srv.PalcoT_NamekuseijinESoldado(eu, +1);
			// A REGUA E A ESCOLHA DA PRESA, e o golpe que a confirma: a vida mede tambem o raspao de uma briga ao lado (ver o contra-exemplo).
			bool mirou = nk > 0 && soldado > 0 && await Ate(() => srv.PalcoT_PresaDe(soldado) == nk, 6);
			bool apanhou = mirou && await Ate(() => srv.PalcoT_Vida(nk) is >= 0 and < 99.9, 12);
			await Segundos(0.3);
			Fotografar("user://tropa-4-ataca-namekuseijin.png");
			Conferir(mirou && apanhou, "R4. O SOLDADO ESCOLHE O NAMEKUSEIJIN (NPC) COMO PRESA e o ataca, na frente do fotografo "
									  + $"[Namekuseijin {nk}, soldado {soldado}; mirou {mirou}; vida {srv.PalcoT_Vida(nk):0.0}]");

			// A CENA DO CONTRA-EXEMPLO E ISOLADA (um soldado, o Namekuseijin fora da linha da briga) e o fotografo fica na carencia: com o defeito
			// o soldado vai pra cima dele, e nas primeiras rodadas o raspao dessa briga arranhava o Namekuseijin ao lado e o golpe arremessava a
			// camera pra longe da cena.
			(int nk2, int soldado2) = srv.PalcoT_CenaDoContraExemplo(eu);
			srv.PalcoT_FotografoNaCarencia(eu, 6);
			srv.PalcoT_DefeitoHostilComum(true);
			try
			{
				bool mirouONamekuseijin = false, mirouOFotografo = false;
				double fim = Time.GetTicksMsec() / 1000.0 + 5;
				while (Time.GetTicksMsec() / 1000.0 < fim)
				{
					int alvo = srv.PalcoT_PresaDe(soldado2);
					mirouONamekuseijin |= alvo == nk2;
					mirouOFotografo |= alvo == eu;
					await Quadros(1);
				}
				Fotografar("user://tropa-4b-defeito-hostil-comum.png");
				Conferir(nk2 > 0 && soldado2 > 0 && !mirouONamekuseijin && mirouOFotografo,
						 "CONTRA-EXEMPLO (defeito injetado: o soldado no ramo do hostil comum, que so ve jogador): ele nunca escolhe o Namekuseijin -- vai "
						 + $"pra cima do fotografo, o unico jogador a vista -- e a regua de R4 reprovaria [mirou o fotografo {mirouOFotografo}; "
						 + $"vida do Namekuseijin {srv.PalcoT_Vida(nk2):0.0}]");
			}
			finally
			{
				srv.PalcoT_DefeitoHostilComum(false);
				srv.PalcoT_FotografoNaCarencia(eu, 0);
			}

			// ---------------------------------------------------------- R5. a esfera do chao
			srv.PalcoT_TirarOPovo();
			srv.PalcoT_DePe(eu);   // no contra-exemplo o soldado, hostil comum, foi pra cima do fotografo
			_ditas.Clear();
			(int comAEsfera, int numero) = srv.PalcoT_EsferaNaFrenteDoSoldado(eu);
			// QUEM PEGOU e o soldado da cena, quase sempre; um que tenha nascido perto no meio dela tambem vale -- a pergunta e "um soldado pegou".
			bool pegou = comAEsfera > 0 && await Ate(() => srv.PalcoT_QuemCarrega() > 0 && Disse("pegou a"), 15);
			int quemPegou = srv.PalcoT_QuemCarrega();
			// A FOTO E LOGO DEPOIS DE PEGAR: a esfera estava a tres tiles do fotografo, e o soldado que a pega sai voando atras da proxima.
			await Segundos(0.2);
			Fotografar("user://tropa-5-pegou-a-esfera.png");
			Conferir(pegou, $"R5. O SOLDADO PEGA A ESFERA DO CHAO, e o chat diz quem pegou [esfera {numero}; com {quemPegou} (o da cena e {comAEsfera}); "
							+ $"ouvi: {UltimasDitas()}]");

			// ---------------------------------------------------------- R6. derrotado, ela cai
			srv.PalcoT_Nocautear(quemPegou);
			bool caiu = await Ate(() => srv.PalcoT_QuemCarrega() == 0 && Disse("cai de")
										 && cli.Esferas.Any(e => e.Tipo == Protocol.CoisaDeEsfera.Esfera && e.Numero == numero), 6);
			await Segundos(0.5);
			Fotografar("user://tropa-6-esfera-caiu.png");
			Conferir(caiu, $"R6. O SOLDADO NOCAUTEADO SOLTA A ESFERA, no chao onde ele esta, e o chat diz [no chao {srv.PalcoT_QuemCarrega() == 0}; ouvi: {UltimasDitas()}]");
		}
		finally { cli.Falou -= Ouvir; }
	}

	/// <summary>O botao de uma saga no cartao Sagas, pelo metadado (`agora_saga` ou `resetar_saga`) e o id dela.</summary>
	private static Button? BotaoDaSaga(Node raiz, string meta, string id) =>
		Todos(raiz).OfType<Button>().FirstOrDefault(b => b.IsVisibleInTree() && b.HasMeta(meta) && b.GetMeta(meta).AsString() == id);

	/// <summary>Rola a aba ate o cartao Sagas ficar na tela.</summary>
	private async Tarefa MostrarOCartaoDasSagas(Node raiz)
	{
		PanelContainer? cartao = Todos(raiz).OfType<PanelContainer>()
			.FirstOrDefault(c => c.IsVisibleInTree() && c.HasMeta("titulo") && c.GetMeta("titulo").AsString() == "Sagas");
		for (Node? p = cartao?.GetParent(); p != null; p = p.GetParent())
			if (p is ScrollContainer rol) { rol.EnsureControlVisible(cartao); break; }
		await Quadros(4);
	}

	/// <summary>Abre o menu P na aba pedida (pelo botao dela, o caminho do dedo) e espera ela montar.</summary>
	private async Tarefa Aba(MenuJogo menu, string aba)
	{
		if (!menu.Visible) menu.Abrir();
		await Quadros(3);
		Button? b = Todos(menu).OfType<Button>().FirstOrDefault(x => x.IsVisibleInTree() && string.Equals(x.Text, aba, StringComparison.OrdinalIgnoreCase));
		b?.EmitSignal(BaseButton.SignalName.Pressed);
		await Quadros(6);
	}

	private static bool NaTela(GameClient cli, ZoneKey z) =>
		cli.Zone.Equals(z) && World.Instancia?.ZonaMontadaDeTeste == z.Hash && TelaDeCarregamento.Instancia?.NoAr != true;

	private static CharacterVisual? Boneco(int id) =>
		(World.Instancia?.FindChild($"Remoto{id}", true, false) as RemotePlayer)?.GetNodeOrNull<CharacterVisual>("Visual");

	/// <summary>O nome da folha do corpo que o boneco veste agora (sem pasta nem extensao), ou "" sem boneco.</summary>
	private static string Folha(int id) => Boneco(id)?.FolhaDoCorpoDeTeste is { Length: > 0 } f ? System.IO.Path.GetFileNameWithoutExtension(f) : "";

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node filho in raiz.GetChildren())
		{
			yield return filho;
			foreach (Node neto in Todos(filho)) yield return neto;
		}
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

	private void Fechar()
	{
		foreach (string l in _linhas) GD.Print("[dtropa] " + l);
		GD.Print(_falhas.Count == 0 ? "[dtropa] ===== TUDO OK =====" : $"[dtropa] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dtropa]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
