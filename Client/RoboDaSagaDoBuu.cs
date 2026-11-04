using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// AS FOTOS DO PEDIDO 218 (`--diagbuu`, 2026-11-01) -- o dono: *"tire foto de tudo pra revisao"*. Com janela e com `--host` (o palco e
/// `GameServer.SagaDoBuuPalco.cs`): o Majin Boo nasce pela cadeia e vem pro lado do jogador, e o servidor anda a saga dele de verdade.
///   R1. o FAT BOO na Terra, neutro (sem presa com o jogador ao lado) -- foto `buu-1-fat-boo`;
///   R2. a DIVISAO: a cena do Super Majin (`buu-2-divisao`, a passagem no meio dela) e o SUPER BOO no fim (`buu-3-super-boo`);
///   R3. o jogador derrubado e ABSORVIDO: dentro do Super Boo, diante da imagem -- `buu-4-dentro-do-boo`;
///   R4. a imagem cai: o jogador sai, e o KID BOO nasce da cena da Forma Pura -- `buu-5-kid-boo`;
///   R5. a ESFERA ROSA sobre o Kid -- `buu-6-esfera-rosa`;
///   R6. a esfera bate: a Terra entra nos 5 minutos de destruicao, e o Kid some pro Ceu -- `buu-7-terra-condenada`;
///   R7. o jogador vai ao Ceu e o Kid esta la -- `buu-8-kid-no-ceu`. No fim a Terra, a cadeia e o jogador voltam ao que eram.
///
///     Godot --path . --host --rede 12871 --horateste 0.5 --semfauna --campoteste 9 --diagbuu --raca Human --conta bancada_buu --nome Goku
/// </summary>
public partial class RoboDaSagaDoBuu : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];

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
		Fechar();
	}

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia != null && S != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo nasceu e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv) return;
		int eu = cli.LocalId;
		await Segundos(1.5);
		GD.Print("[dbuu] ===== AS FOTOS DO MAJIN BOO: GORDO, SUPER E KID (dono, 2026-11-01, o pedido 218) =====");
		try
		{
			// ---------------------------------------------------------------- R1: o Fat Boo, neutro
			int boo = srv.PalcoBoo_Nascer(eu);
			await Segundos(3.0);
			var e1 = srv.PalcoBoo_Estado();
			Fotografar("buu-1-fat-boo");
			Conferir(boo != 0 && e1.Degrau == SagaDoBuu.DegrauGordo && e1.Corpo == SagaDoBuu.CorpoDoFatBoo && e1.Presa == 0 && e1.Letal,
					 $"R1. O FAT BOO na Terra, com o corpo do Fat Boo do jogador, NEUTRO com o jogador ao lado [degrau {e1.Degrau}, corpo {e1.Corpo}, "
					 + $"presa {e1.Presa}, zona {e1.Zona}]");

			// ---------------------------------------------------------------- R2: a divisao
			srv.PalcoBoo_Dividir();
			bool dividiu = await Ate(() => srv.PalcoBoo_Estado().Degrau == SagaDoBuu.DegrauSuper, 5);
			await Segundos(15.5);
			Fotografar("buu-2-divisao");
			// (r4) A PASSAGEM DA DIVISAO SEM O CORTE DA BORDA: o dono, na foto da r2: *"Parece q ta cortado o sprite do fat com o evil"*. A arte da
			// `super_majin_transform` faz a metade rosa entrar pela borda do quadro de 32 px; tocada no de 64 (`PassagemSemCorte`), ela entra inteira.
			CharacterVisual? doBoo = World.Instancia?.CorpoDeTeste(boo)?.GetNodeOrNull<CharacterVisual>("Visual");
			string passagem = doBoo?.PassagemDeTeste ?? "-";
			int largura = doBoo?.LarguraDaPassagemDeTeste ?? 0;
			Conferir(passagem == PassagemSemCorte.Animacao && largura == 64,
					 $"R2a. A PASSAGEM DA DIVISAO SEM CORTE: a `{PassagemSemCorte.Animacao}` toca no quadro de 64 px, e a metade rosa entra inteira "
					 + $"[passagem `{passagem}`, quadro de {largura} px]");
			// (r2) O FOTOGRAFO NA CARENCIA: a foto do Super Boo agora espera a poeira assentar, e ele, solto e agressivo, me derrubaria antes.
			srv.PalcoBoo_FotografoNaCarencia(eu, 30);
			await Ate(() => !srv.PalcoBoo_Estado().EmCena, 15);
			// (r2) E A CENA ACABADA NA MINHA TELA, nao so no servidor: na r1 a foto saiu no fim da cena do servidor, com a nuvem do estouro
			// cobrindo o Super Boo. Agora: a cena acabou aqui, o corpo desenhado e o da forma 3, e a poeira teve tempo de assentar.
			string folhaDoSuper = Jandirus.Core.Races.SagaDoMajin.FolhaDoCorpo(SagaDoBuu.CorpoDoSuperBoo) ?? "?";
			bool superNaTela = await Ate(() => CenaAcabouNaTela(boo) && NaTela(boo) && FolhaNaTela(boo) == folhaDoSuper, 20);
			await Segundos(SegundosDaPoeira);
			Fotografar("buu-3-super-boo");
			var e2 = srv.PalcoBoo_Estado();
			Conferir(dividiu && e2.Corpo == SagaDoBuu.CorpoDoSuperBoo && !e2.Letal && superNaTela,
					 $"R2. A DIVISAO: a cena do Super Majin e o SUPER BOO, de golpe nao-letal -- NA MINHA TELA com o corpo da forma 3, a cena acabada "
					 + $"[degrau {e2.Degrau}, corpo {e2.Corpo}, letal {e2.Letal}; tela: {NaTelaDiag(boo)}]");

			// ---------------------------------------------------------------- R2b: o raio do Super Boo sai de lado (r3)
			// O dono viu o Super Boo soltar o Kamehameha pra esquerda virado pra camera, com o vapor saindo da cabeca: *"ele soltando
			// kamehameha ta na animação errada"*. O `blast` da folha da forma 3 e de UMA direcao (o vapor do `Enrage`); a pose de tiro agora
			// e o soco com lado (`CharacterVisual.Escolher`). Um boneco do Super Boo nas quatro direcoes, e a foto dele virado pro oeste.
			(bool deLado, string poses) = await TiroDoSuperDeLado();
			Conferir(deLado, $"R2b. O RAIO DO SUPER BOO SAI DE LADO: atirando, o corpo da forma 3 veste o soco virado pro rumo (`attack_*`), e nao "
							 + $"o vapor da cabeca (`blast`) [{poses}]");

			// ---------------------------------------------------------------- R3: absorvido
			srv.PalcoBoo_Derrubar(eu);
			bool dentro = await Ate(() => InteriorDoMajin.EhOInterior(cli.Zone), 10);
			// (r2) A IMAGEM NA MINHA TELA, e a foto JA -- a `dmajin-2-dentro` do `RoboDaAbsorcaoMajin` faz igual: em menos de dois segundos ela
			// esta em cima de mim. Na r1 a foto saiu 3 s depois e SEM a imagem, que tinha entrado no meu campo de visao; a frase diz onde ela
			// esta, na minha tela e no servidor.
			int imagem = srv.PalcoBoo_Imagem();
			bool imagemNaTela = await Ate(() => NaTela(imagem), 8);
			await Segundos(0.3);
			Fotografar("buu-4-dentro-do-boo");
			Conferir(dentro && srv.PalcoBoo_Estado().Dentro == 1 && imagemNaTela,
					 $"R3. O JOGADOR DERRUBADO E ABSORVIDO: dentro do Super Boo, diante da imagem -- e ela NA MINHA TELA [zona {cli.Zone.Name}, "
					 + $"dentro {srv.PalcoBoo_Estado().Dentro}; tela: {NaTelaDiag(imagem)}; servidor: {srv.PalcoBoo_DaImagem(eu)}]");

			// ---------------------------------------------------------------- R4: a imagem cai, o Kid nasce
			srv.PalcoBoo_VencerAImagem();
			bool saiu = await Ate(() => !InteriorDoMajin.EhOInterior(cli.Zone), 8);
			bool kid = await Ate(() => srv.PalcoBoo_Estado().Degrau == SagaDoBuu.DegrauKid, 5);
			// (r3) O KID AO MEU LADO pras fotos dele e da esfera: o Boo me cuspiu no chao seco mais perto dele, e na r2 isso foi a 19 tiles.
			// (2026-11-02, a r6 da gosma) EU PARADO ANTES DE TRAZER O KID: o diagnostico da r4 e da r5 mostrou o Kid onde o palco o pos e
			// EU 13-15 tiles adiante, cada vez numa direcao (o arremesso de um golpe da imagem seguia depois da saida). Trazido antes de eu
			// parar, ele ficava pra tras, fora da foto.
			await EuParadoNoServidor(srv, eu, 4.0);
			bool trouxe = srv.PalcoBoo_TrazerOKid(eu);
			// (2026-11-02, a r5 da gosma) E PARADO AO MEU LADO ate o fim: solto, ele saia atras de outra presa (a 11-15 tiles na r3 e na r4) e
			// no Ceu me derrubava -- a fuga, a esfera e o salto pro Ceu sao do relogio da saga, e continuam. Ver `PalcoBoo_SemCerebro`.
			srv.PalcoBoo_SemCerebro(true);
			// (r2) A CENA DA FORMA PURA ACABADA NA MINHA TELA, o corpo do Kid desenhado e a poeira assentada -- na r1 a foto saiu com a nuvem
			// cobrindo o Kid. A esfera nasce no fim da cena, e o relogio dela comeca a contar quando ela aparece (`VerAEsfera`).
			string folhaDoKid = SagaDoBuu.FolhaDoCorpo(SagaDoBuu.CorpoDoKidBoo) ?? "?";
			bool kidNaTela = await Ate(() => { VerAEsfera(); return CenaAcabouNaTela(boo) && NaTela(boo) && FolhaNaTela(boo) == folhaDoKid; }, 20);
			// (a r6 da gosma) ...e, se mesmo assim eu me afastei dele, ele vem de novo antes da foto
			if (!kidNaTela && srv.PalcoBoo_NoServidor(eu).Tiles > 5)
			{
				await EuParadoNoServidor(srv, eu, 3.0);
				trouxe = srv.PalcoBoo_TrazerOKid(eu);
				kidNaTela = await Ate(() => { VerAEsfera(); return CenaAcabouNaTela(boo) && NaTela(boo) && FolhaNaTela(boo) == folhaDoKid; }, 6);
			}
			await Ate(() => { VerAEsfera(); return false; }, SegundosDaPoeira);
			Fotografar("buu-5-kid-boo");
			Conferir(saiu && kid && trouxe && srv.PalcoBoo_Estado().Corpo == SagaDoBuu.CorpoDoKidBoo && kidNaTela,
					 $"R4. A IMAGEM CAI: o jogador sai e o KID BOO nasce -- NA MINHA TELA (no quadro da foto) com o corpo da Forma Pura, a cena acabada "
					 + $"[saiu {saiu}, trazido {trouxe}, degrau {srv.PalcoBoo_Estado().Degrau}, corpo {srv.PalcoBoo_Estado().Corpo}; tela: {NaTelaDiag(boo)}; "
					 + $"{NoServidorDiag(srv, eu)}]");

			// ---------------------------------------------------------------- R5: a esfera rosa
			// (r3) ATE VINTE SEGUNDOS: sem a cena na minha tela (o Kid nasceu longe e o palco o trouxe), a foto do Kid sai antes de a cena do
			// servidor acabar -- e e no fim dela que a esfera nasce.
			bool esfera = await Ate(VerAEsfera, 20);
			// (r2) SEIS SEGUNDOS DE CARGA contados de quando ela APARECEU (a r1 contava de quando o robo olhou, e a foto do Kid agora espera a
			// poeira): grande, e ainda sem cair.
			await Segundos(Math.Max(0.5, _esferaDesde + 6.0 - Agora));
			Fotografar("buu-6-esfera-rosa");
			bool rosa = Esfera()?.PintadaDeRosaDeTeste == true;
			// (r3) E NO QUADRO DA FOTO: na r2 ela existia, rosa, a 19 tiles -- e a foto saiu igual a do Kid, so com o jogador.
			// (r4) PELO CIRCULO, e nao pelo centro: ela e gigante, e o centro sobe acima da tela -- na r3 a foto saiu com ela inteira em cima
			// do Kid, e a regua do ponto no quadro reprovou.
			(bool noQuadro, string ondeNaFoto) = Esfera() is { } s ? EsferaNoQuadro(s) : (false, "sem esfera");
			Conferir(esfera && rosa && noQuadro,
					 $"R5. A ESFERA ROSA sobe sobre o Kid (a Death Ball, pintada de rosa), no quadro da foto [na tela {esfera}, rosa {rosa}, "
					 + $"no quadro {noQuadro}: {ondeNaFoto}]");

			// ---------------------------------------------------------------- R6: a Terra condenada, o Kid no Ceu
			bool condenada = await Ate(() => srv.PalcoBoo_TerraCondenada(), 20);
			await Segundos(0.8);
			Fotografar("buu-7-terra-condenada");
			bool noCeu = await Ate(() => srv.PalcoBoo_Estado().Zona == SagaDoBuu.ZonaDoKid, 6);
			Conferir(condenada && noCeu,
					 $"R6. A ESFERA BATE: a Terra entra nos 5 minutos de destruicao, e o Kid salta pro Ceu [condenada {condenada}, Kid em {srv.PalcoBoo_Estado().Zona}]");

			// ---------------------------------------------------------------- R7: o Kid no Ceu
			bool foi = srv.PalcoBoo_IrProCeu(eu);
			bool chegou = await Ate(() => cli.Zone.Name == SagaDoBuu.ZonaDoKid, 15);
			// (r2) O KID NA MINHA TELA, e eu VIVO na foto: o palco me leva na carencia (`PalcoBoo_IrProCeu`) -- na r1 o Kid me matou em 4 s e
			// a foto saiu na mesa do Enma, sem Kid nenhum.
			bool kidNoCeu = await Ate(() => NaTela(boo) && FolhaNaTela(boo) == folhaDoKid, 10);
			await Segundos(1.0);
			Fotografar("buu-8-kid-no-ceu");
			bool dePe = srv.PalcoBoo_JogadorDePe(eu) && cli.Zone.Name == SagaDoBuu.ZonaDoKid;
			Conferir(foi && chegou && kidNoCeu && dePe,
					 $"R7. O JOGADOR VAI AO CEU e o Kid esta la: NA MINHA TELA, comigo vivo e de pe na foto [foi {foi}, zona {cli.Zone.Name}, "
					 + $"de pe {dePe}; tela: {NaTelaDiag(boo)}]");
		}
		finally
		{
			srv.PalcoBoo_Desfazer(eu);
		}
	}

	/// <summary>A esfera rosa desenhada no mundo, ou nula.</summary>
	private static SupernovaDesenhada? Esfera() =>
		World.Instancia is { } w ? Achar<SupernovaDesenhada>(w, s => s.Ataque == AtaqueDoUltimato.EsferaRosa) : null;

	/// <summary>
	/// (r3) O BONECO DO SUPER BOO ATIRANDO nas quatro direcoes: a animacao do corpo tem de ser o `attack_<rumo>`. Ele fica numa camada de
	/// tela, tres tiles a direita do meio (onde a camera poe o jogador), no tamanho do mundo -- pra foto `buu-3b-super-atira`, virado pro
	/// oeste -- e sai no fim.
	/// </summary>
	private async System.Threading.Tasks.Task<(bool Todas, string Poses)> TiroDoSuperDeLado()
	{
		var camada = new CanvasLayer { Name = "BonecoDoSuper", Layer = 5 };
		var v = new CharacterVisual { Name = "Super" };
		camada.AddChild(v);
		AddChild(camada);
		try
		{
			VisualCatalog cat = VisualCatalog.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/visual.json"));
			v.Vestir(cat, new Appearance { Corpo = SagaDoBuu.CorpoDoSuperBoo }, "Majin", "Male");
			float zoom = GetViewport().GetCamera2D()?.Zoom.X ?? 2f;
			v.Scale = Vector2.One * zoom;
			v.Position = GetViewport().GetVisibleRect().Size / 2 + new Vector2(3 * ZoneCollision.TileSize * zoom, 0);
			var poses = new List<string>();
			bool todas = true;
			foreach (Facing rumo in new[] { Facing.South, Facing.North, Facing.East, Facing.West })
			{
				v.SetMotion(rumo, false);
				v.SetPoseDeTeste(Protocol.Pose.Canalizando, canalAtirando: true);
				await Quadros(3);
				string anim = v.AnimacaoDeTeste;
				poses.Add($"{rumo} `{anim}`");
				todas &= anim == "attack_" + MoveRules.FacingSuffix(rumo);
			}
			await Segundos(0.3);
			Fotografar("buu-3b-super-atira");
			return (todas, string.Join(", ", poses));
		}
		finally
		{
			camada.QueueFree();
		}
	}

	/// <summary>(r2) Quanto a poeira do estouro de uma cena leva pra assentar: a nuvem vive 1,9 s (`PoeiraDeEstrago.Duracao`), com folga.</summary>
	private const double SegundosDaPoeira = 3.0;

	/// <summary>(r2) Quando a esfera rosa apareceu na minha tela, no relogio de `Agora` (-1 = ainda nao).</summary>
	private double _esferaDesde = -1;

	private static double Agora => Time.GetTicksMsec() / 1000.0;

	/// <summary>(r2) Olha a esfera rosa e anota a PRIMEIRA vez que ela aparece -- a foto dela conta a carga dali.</summary>
	private bool VerAEsfera()
	{
		if (_esferaDesde < 0 && Esfera() != null) _esferaDesde = Agora;
		return _esferaDesde >= 0;
	}

	/// <summary>(r2) A folha do corpo deste id desenhado na minha tela ("" = nao desenhado).</summary>
	private static string FolhaNaTela(int id) =>
		World.Instancia?.CorpoDeTeste(id)?.GetNodeOrNull<CharacterVisual>("Visual")?.FolhaDoCorpoDeTeste ?? "";

	/// <summary>(r2) Este corpo esta no meu mundo, VISIVEL, com a folha carregada -- e (r3) DENTRO DO QUADRO da foto?</summary>
	private static bool NaTela(int id) =>
		World.Instancia?.CorpoDeTeste(id) is { } c && c.IsVisibleInTree() && FolhaNaTela(id).Length > 0 && NoQuadro(c, MargemDoQuadro);

	/// <summary>(r3) Quanto o pe de um corpo tem de estar pra dentro da borda da foto: o boneco sobe dele, e um pe na beirada sai cortado.</summary>
	private const float MargemDoQuadro = 48f;

	/// <summary>
	/// (r3) O PONTO DESTE NODE CAI DENTRO DA FOTO? Na r2 o Kid "estava na tela" -- desenhado e visivel -- a 19 tiles de mim, fora do quadro, e
	/// a foto saiu so comigo. O quadro e o da viewport, no espaco do `GetGlobalTransformWithCanvas` (a camera ja aplicada).
	/// </summary>
	private static bool NoQuadro(Node2D n, float margem) =>
		n.GetViewport() is { } v && v.GetVisibleRect().Grow(-margem).HasPoint(n.GetGlobalTransformWithCanvas().Origin);

	/// <summary>
	/// (r4) O CIRCULO DA ESFERA CRUZA A FOTO? Ela e gigante e o centro dela sobe acima da tela -- a regua do ponto (`NoQuadro`) reprovou a r3
	/// com a esfera inteira na foto, em cima do Kid. Conta o ponto do quadro mais perto do centro: dentro do raio, ela aparece.
	/// </summary>
	private static (bool Cruza, string Onde) EsferaNoQuadro(SupernovaDesenhada s)
	{
		if (s.GetViewport() is not { } v) return (false, "sem viewport");
		Transform2D t = s.GetGlobalTransformWithCanvas();
		Vector2 centro = t * s.CentroDeTeste;
		float raio = s.DiametroDeTeste / 2f * t.Scale.X;
		Rect2 quadro = v.GetVisibleRect();
		Vector2 perto = centro.Clamp(quadro.Position, quadro.End);
		bool cruza = raio > 0 && centro.DistanceTo(perto) <= raio;
		return (cruza, $"centro px {centro.X:0},{centro.Y:0}, raio {raio:0} px, quadro {quadro.Size.X:0}x{quadro.Size.Y:0}");
	}

	/// <summary>(r2) A cena de forma deste corpo acabou na minha tela (a do servidor acaba antes da poeira do estouro aqui)?</summary>
	private static bool CenaAcabouNaTela(int id) => World.Instancia?.CenaEmCursoDeTeste(id) == null;

	/// <summary>
	/// (2026-11-02, a r6 da gosma) ESPERA A MINHA POSICAO NO SERVIDOR PARAR por meio segundo (no maximo <paramref name="teto"/> s):
	/// quem sai de dentro do Boo pode estar no meio de um arremesso, e o Kid trazido nessa hora fica pra tras.
	/// </summary>
	private async Tarefa EuParadoNoServidor(Jandirus.Server.GameServer srv, int eu, double teto)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + teto;
		Vec2 antes = srv.PalcoBoo_NoServidor(eu).Eu;
		double paradoDesde = Time.GetTicksMsec() / 1000.0;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			await Quadros(1);
			Vec2 agora = srv.PalcoBoo_NoServidor(eu).Eu;
			double t = Time.GetTicksMsec() / 1000.0;
			if ((agora - antes).Length > 0.05f) { antes = agora; paradoDesde = t; }
			else if (t - paradoDesde >= 0.5) return;
		}
	}

	/// <summary>
	/// (2026-11-01, a r4 da gosma) O BOO E EU NO SERVIDOR, e o quanto o MEU corpo desenhado esta longe de onde o servidor me tem -- as duas
	/// hipoteses do Kid a 15 tiles da r3 (ele andou, ou eu estou desenhado em outro lugar).
	/// </summary>
	private static string NoServidorDiag(Jandirus.Server.GameServer srv, int eu)
	{
		(double tiles, Vec2 meu, Vec2 doBoo) = srv.PalcoBoo_NoServidor(eu);
		if (tiles < 0) return "servidor: o Boo em outra zona";
		string desenho = World.Instancia?.PosicaoDesenhadaDe(eu) is { } d
			? $"{new Vector2(d.X / ZoneCollision.TileSize - meu.X, d.Y / ZoneCollision.TileSize - meu.Y).Length():0.0}" : "?";
		return $"servidor: o Boo a {tiles:0.0} tiles de mim (eu em {meu.X:0.0},{meu.Y:0.0}; ele em {doBoo.X:0.0},{doBoo.Y:0.0}); "
			   + $"o meu corpo desenhado a {desenho} tiles de onde o servidor me tem";
	}

	/// <summary>(r2) O que a minha tela sabe de um corpo, pra frase de uma falha.</summary>
	private static string NaTelaDiag(int id)
	{
		if (World.Instancia is not { } w || w.CorpoDeTeste(id) is not { } c) return $"#{id} fora do meu mundo";
		Vector2? eu = w.PosicaoDesenhadaDe(GameClient.Instance?.LocalId ?? 0);
		string longe = eu is { } p ? $"{c.Position.DistanceTo(p) / ZoneCollision.TileSize:0.0} tiles de mim" : "longe ?";
		return $"#{id} no meu mundo, visivel {c.Visible}/{c.IsVisibleInTree()}, a {longe}, no quadro {NoQuadro(c, MargemDoQuadro)} "
			   + $"(px {c.GetGlobalTransformWithCanvas().Origin.X:0},{c.GetGlobalTransformWithCanvas().Origin.Y:0}), "
			   + $"cena {(CenaAcabouNaTela(id) ? "acabada" : "EM CURSO")}, folha `{FolhaNaTela(id)}`";
	}

	// =====================================================================
	// AS MAOS
	// =====================================================================
	private static T? Achar<T>(Node raiz, Func<T, bool> serve) where T : Node
	{
		if (raiz is T t && !t.IsQueuedForDeletion() && serve(t)) return t;
		foreach (Node filho in raiz.GetChildren())
			if (Achar(filho, serve) is { } achado) return achado;
		return null;
	}

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { _linhas.Add("  --     sem foto (headless nao renderiza)"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
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
		foreach (string l in _linhas) GD.Print("[dbuu] " + l);
		GD.Print(_falhas.Count == 0 ? "[dbuu] ===== TUDO OK =====" : $"[dbuu] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dbuu]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
