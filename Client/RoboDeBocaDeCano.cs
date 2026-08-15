using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A BOCA DO CANO, FOTOGRAFADA (`--diagboca`).
///
/// ============================ O PEDIDO DO DONO, E ELE VEIO EM FOTO ============================
/// *"os beams tao saindo DE CIMA do personagem, deveriam sair DA FRENTE dele, NA FRENTE DO SPRITE
/// deles"* -- com uma imagem do feixe comecando acima do corpo, o balao "Kamehameha!!" em cima.
///
/// SAO DUAS PERGUNTAS E ELE FEZ AS DUAS. **ONDE nasce** (a boca do cano devia sair da mao, a frente
/// do corpo, no sentido em que ele olha) e **EM QUE CAMADA e desenhado** ("na frente do sprite"
/// tambem se le como ordem de desenho -- o feixe passando POR TRAS do corpo em vez de a frente
/// dele). Esta bancada mede as duas, separadas, nos quatro sentidos, e diz qual estava errada.
/// ==========================================================================================
///
/// ============================ POR QUE ELA EXISTE SE A `--projetilteste` JA E VERDE ============================
/// A familia 1-bis da `--projetilteste` mede a boca em NUMERO -- projecao no rumo, deriva
/// transversal, os quatro sentidos, o vao entre os quadros -- e nao e pouco. Mas ela le o `Pos` do
/// SERVIDOR, e a queixa e de TELA. As tres coisas que ela nao pode ver:
///
///   1. **a CAMADA**: `ZIndex` nao e campo de servidor. Um feixe desenhado atras do corpo tem o
///      `Pos` perfeito.
///   2. **a ALTURA**: o campo `Altitude` existia, valia pra colisao e **nao chegava ao desenho** --
///      o feixe de quem voava era desenhado no plano do chao, ate 160 px abaixo do proprio corpo.
///      Verde nos dois lados, errado na tela. Ja aconteceu, neste mesmo campo.
///   3. **a CAUDA de um raio canalizado**: quem fica colado no corpo nao e a cabeca, e o pedaco
///      `origin`, que mora na cauda -- e a cauda e reescrita a cada tique.
///
/// ============================ COMO ELA MEDE O FEIXE SEM MEDIR O BONECO ============================
/// A mascara NAO e "foto com tiro menos foto sem tiro tirada um segundo antes". Nao pode ser: ao
/// canalizar, o corpo TROCA DE POSE (o pedido antigo do dono, a `--diagpose`), o chao muda com o
/// estrago do proprio tiro e a hora do mundo anda. Uma mascara dessas traria o boneco inteiro dentro
/// dela -- justamente a regiao que esta bancada precisa medir vazia.
///
/// Entao as fotos sao do MESMO INSTANTE: com a arvore PAUSADA, esconde-se **o node do tiro**
/// (`Visible = false`), fotografa-se, mostra-se, fotografa-se, esconde-se e fotografa-se de novo.
/// Nada mais no mundo mudou entre elas -- nem a pose, nem a hora, nem o chao. O que difere e, por
/// construcao, **o que aquele tiro pintou**.
///
/// ============================ E SAO TRES FOTOS, NAO DUAS -- A PRIMEIRA RODADA COBROU ============================
/// Com duas (com e sem), esta bancada reprovou o Leste e o Oeste medindo `44 px de feixe dentro do
/// boneco` e `516 px atras do corpo`. Olhando a foto: **o fundo nao para**. O berco e uma lingua de
/// areia cercada de agua, a agua e animada, o balao de fala desbota, o proprio feixe cava o chao e os
/// NPCs que ele acerta sangram. Nem tudo isso obedece a pausa da arvore -- shader anda com `TIME`, que
/// e do renderizador e nao do laco de jogo.
///
/// A terceira foto resolve sem depender de a pausa alcancar cada efeito: um pixel so conta como tinta
/// de feixe se ele **diferir das DUAS fotos sem tiro** e se **as duas fotos sem tiro concordarem entre
/// si** naquele ponto. Onde o fundo se mexeu sozinho elas discordam, e aquele pixel sai da conta -- que
/// e a leitura honesta de "nao da pra saber quem pintou aqui". A mascara resultante e GRAVADA em foto
/// (`boca-*-mascara.png`), porque uma sonda que conta pixels tem que mostrar QUAIS pixels ela contou.
/// ==========================================================================================================
///
/// COMO RODAR -- um processo so, com JANELA (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 7932 --vooteste --bpteste 3000000 --horateste 0.5 \
///           --diagboca --position 1920,0 --resolution 1600x900 \
///           --raca Human --conta bancada_boca --nome Boqueiro
///
/// `--horateste 0.5` crava MEIO-DIA e isso nao e enfeite: de dia a <see cref="LuzDeKi"/> nao acende
/// (`Iluminacao.ForcaDaNoite`), e uma luz radial de tres tiles em volta da cabeca do tiro entraria na
/// mascara como se fosse tinta de feixe -- inclusive por cima do corpo, que e o que se mede aqui.
/// `--vooteste` da a skill de voo, sem a qual a cena C nao decola.
///
/// As fotos saem em `user://boca-*.png`.
/// </summary>
public partial class RoboDeBocaDeCano : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	// =====================================================================
	// OS NUMEROS DA MEDIDA
	// =====================================================================
	/// <summary>
	/// METADE DO SPRITE, em pixel de MUNDO. O corpo e uma folha de 32x32 centrada no `Pos` (o `Pos`
	/// deste port e o CENTRO do sprite -- a caixa dos pes desce `MoveRules.FeetOffsetY` a partir dele),
	/// entao a caixa do boneco vai de -16 a +16 nos dois eixos em volta do ponto desenhado.
	/// </summary>
	private const float MeioCorpo = ZoneCollision.TileSize / 2f;

	/// <summary>
	/// ONDE COMECA "EM CIMA DELE", em pixel de mundo -- e ela e MENOR que <see cref="MeioCorpo"/> de
	/// proposito, por um pixel.
	///
	/// ============================ ENCOSTAR NA FRENTE E O PEDIDO; COBRIR E O DEFEITO ============================
	/// A boca do cano fica a 32 px do centro e a arte do pedaco `origin` e uma celula de 32 centrada
	/// nela: a borda de tras dessa celula cai em **exatamente 16 px**, que e a borda da frente do
	/// sprite. Elas se encostam por construcao -- e e isso que o dono pediu ("sair DA FRENTE dele").
	///
	/// Medindo com a borda inclusa, a bancada reprovou o Sul com `36 px dentro do boneco`: a linha de
	/// pixels da junta, meio pixel de rasterizacao pra dentro. Contar aquilo como "feixe em cima do
	/// personagem" seria a bancada reprovando o comportamento correto -- e um pixel e o orcamento do
	/// arredondamento, o mesmo raciocinio que a `ProjetilDesenhado.Margem` ja teve que fazer.
	/// ======================================================================================================
	/// </summary>
	private const float DentroDoBoneco = MeioCorpo - 1f;

	/// <summary>
	/// O LADO DO RECORTE DE MEDIDA, em pixel de MUNDO: quatro tiles em volta do corpo.
	///
	/// A pergunta e sobre a VIZINHANCA do personagem ("o feixe encosta nele?"), e nao sobre onde a
	/// cabeca foi parar trinta tiles adiante. Medir a tela inteira so acrescentaria o rastro distante
	/// -- que nao esta em duvida -- e faria a conta de "quanto da tinta esta atras do corpo" depender
	/// do tamanho da janela.
	/// </summary>
	private const float LadoDaMedidaEmMundo = 4 * ZoneCollision.TileSize;

	/// <summary>
	/// DIFERENCA DE CANAL a partir da qual dois pixels contam como DIFERENTES -- o mesmo 0,12 da
	/// bancada da variedade, e pela mesma razao: abaixo disso e ruido de compressao do viewport.
	/// </summary>
	private const float Epsilon = 0.12f;

	/// <summary>Quantos tiles a cabeca anda antes do obturador. Tres: ha trem, e ele cabe na tela.</summary>
	private const double TilesDoObturador = 3.0;

	/// <summary>Depois disto ela desiste -- o berco pode nao ter achado praca nenhuma.</summary>
	private const double Paciencia = 420;

	/// <summary>A altura da cena do voo, em pixel de mundo. Oito tiles: 64 px de subida na tela.</summary>
	private const float AlturaDaCena = 8 * ZoneCollision.TileSize;

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];

	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	/// <summary>Os quatro sentidos, e o indice do da vez.</summary>
	private static readonly Facing[] Sentidos = [Facing.South, Facing.East, Facing.North, Facing.West];
	private int _iSentido;

	private int _vitima;
	private readonly List<Image> _tiraDosQuatro = [];

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Ready()
	{
		// A ARVORE E PAUSADA PRA FOTOGRAFAR O PAR (ver `Obturador`). Sem isto a propria bancada
		// congelaria junto e o par nunca se fecharia.
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagboca` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s)"); Fechar(); return; }

		_t += delta;

		switch (_passo)
		{
			case 0: Assentar(mundo, srv, cli); break;

			// ---- CENA A: os quatro sentidos
			case 1: A_Virar(mundo, srv, cli); break;
			case 2: A_Reancorar(mundo, srv, cli); break;
			case 3: A_Atirar(srv, cli); break;
			case 4: A_Fotografar(mundo, srv, cli); break;

			// ---- CENA B: o defeito injetado no MESMO sentido
			case 5: B_Atirar(srv, cli); break;
			case 6: B_Boa(mundo, srv, cli); break;
			case 7: B_Injetada(mundo, srv, cli); break;

			// ---- CENA C: voando
			case 8: C_Subir(mundo, srv, cli); break;
			case 9: C_Atirar(srv, cli); break;
			case 10: C_NoAr(mundo, srv, cli); break;
			case 11: C_Injetada(mundo, srv, cli); break;

			// ---- CENA D: a camada
			case 12: D_Plantar(mundo, srv, cli); break;
			case 13: D_NaFrente(mundo, srv, cli); break;
			case 14: D_Injetada(mundo, srv, cli); break;

			// ---- CENA E: a colisao colada
			case 15: E_Plantar(srv, cli); break;
			case 16: E_Acertar(mundo, srv, cli); break;
			case 17: E_Injetada(srv, cli); break;

			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	// =====================================================================
	// 0) O BERCO ASSENTA
	// =====================================================================
	/// <summary>
	/// O corpo aprende o catalogo (pelo mesmo `ArmarParaAVariedade` da bancada irma -- o tiro tem que
	/// sair pelo VERB, e sem livro nao ha verb) e vai pra uma PRACA: esta bancada atira pros quatro
	/// lados, e um corredor so promete chao livre pra um.
	/// </summary>
	private void Assentar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 3) return;

		(int skills, int verbos) = srv.ArmarParaAVariedade(cli.LocalId);
		Conferir(skills > 0 && verbos > 0,
			$"o corpo aprendeu o catalogo ({skills} skills, {verbos} verbos) -- o tiro sai pelo VERB");

		bool praca = srv.AssentarNaBoca(cli.LocalId, 6);
		Conferir(praca, "achei uma PRACA com os quatro lados livres (seis tiles em cada)");
		if (!praca) { Fechar(); return; }

		srv.CravarMeioDiaDaVariedade(cli.LocalId);
		Nota("meio-dia cravado: de dia a `LuzDeKi` nao acende, e a mascara mede tinta e nao luz");

		Virar(1);
	}

	// =====================================================================
	// A) OS QUATRO SENTIDOS
	// =====================================================================
	/// <summary>
	/// O CORPO VIRA ANDANDO, e nao ha outro jeito honesto: a direcao do corpo LOCAL e previsao do
	/// cliente (o `LocalPlayer` escreve o proprio `Facing` a partir do input) e escrever so o
	/// `pl.Facing` do servidor produz uma discordancia que so a bancada consegue criar -- a `--diagpose`
	/// ja reprovou exatamente assim, com o servidor dizendo `East` e o desenho dizendo `blast_south`.
	///
	/// ============================ E ELA ANDA ATE O SERVIDOR CONCORDAR, E NAO POR UM RELOGIO ============================
	/// A primeira rodada andava 0,12 s e seguia em frente. O NORTE nao pegou: o servidor continuou
	/// dizendo `East`, e a bancada fotografou um tiro pro leste com o rotulo "North". O passo curto foi
	/// deliberado (este corpo esta armado pra bancada -- com `speed` em 400 ele atravessa a praca em meio
	/// segundo), mas curto demais pra o piloto automatico do cliente sequer montar o caminho.
	///
	/// O certo nao e um numero maior: e olhar. Anda-se ATE o servidor dizer que virou, e para-se no
	/// quadro em que ele diz -- o mais curto possivel, e sem depender de quanto este corpo corre.
	/// ==========================================================================================================
	/// </summary>
	private void A_Virar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		Facing alvo = Sentidos[_iSentido];

		if (!_parouDeVirar)
		{
			(_, Facing olhar, _, _) = srv.CorpoDaBoca(cli.LocalId);
			if (olhar != alvo && _t < 2.0)
			{
				Vec2 d = MeleeArea.Frente(alvo);
				mundo.AndarDeTeste(new Vector2(d.X, d.Y));
				return;
			}

			// O PE SAI DO ACELERADOR NO QUADRO EM QUE O SERVIDOR CONCORDA -- e ai se espera o corpo
			// parar de verdade (o passo tem inercia de rede) antes de repor na ancora.
			mundo.PararDeTeste();
			_parouDeVirar = true;
			_t = 0;
			return;
		}

		if (_t < 0.35) return;

		_parouDeVirar = false;
		srv.ReporNaBoca(cli.LocalId);
		Virar(2);
	}

	private bool _parouDeVirar;

	/// <summary>
	/// A CORRECAO DE POSICAO PRECISA CHEGAR. `ReporNaBoca` escreve no servidor; o corpo local so volta
	/// pra praca quando a correcao atravessa o fio -- e uma foto tirada antes disso mostraria o corpo
	/// meio tile adiante, com a boca do cano medida contra o lugar errado.
	/// </summary>
	private void A_Reancorar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.7) return;

		Facing alvo = Sentidos[_iSentido];
		(Vec2 pos, Facing olhar, _, _) = srv.CorpoDaBoca(cli.LocalId);

		Conferir(olhar == alvo,
			$"{alvo}: o corpo ANDOU pra ca e as duas pontas concordam sobre o olhar (servidor diz {olhar})");

		if (mundo.PosicaoDesenhadaDe(cli.LocalId) is { } desenhado)
		{
			float erro = desenhado.DistanceTo(new Vector2(pos.X, pos.Y));
			Conferir(erro < 6f,
				$"{alvo}: ...e o corpo DESENHADO esta onde o servidor diz que ele esta ({erro:0.0} px de erro)");
		}

		Virar(3);
	}

	private void A_Atirar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.RegarOKiDaVariedade(cli.LocalId);
		srv.LimparOsTirosDaBoca(cli.LocalId);

		// O TIRO SAI PELO BOTAO -- `UsarHabilidade`, o mesmo que o `C2S.Habilidade` aciona. E o
		// Kamehameha de proposito: e a tecnica do balao na foto do dono.
		string resposta = srv.GatilhoDaVariedade(cli.LocalId, "Kamehameha");
		if (resposta.Length > 0) Nota($"{Sentidos[_iSentido]}: o servidor respondeu \"{resposta}\"");

		Virar(4);
	}

	private void A_Fotografar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.RegarOKiDaVariedade(cli.LocalId);
		if (!Obturador(mundo, srv, cli, out Par par)) return;

		Facing lado = Sentidos[_iSentido];
		if (par.Falhou)
		{
			Conferir(false, $"{lado}: o Kamehameha saiu e andou {TilesDoObturador:0} tiles ({par.Porque})");
			Adiante();
			return;
		}

		Medida m = Medir(ref par, $"boca-1-{lado.ToString().ToLowerInvariant()}-mascara.png");
		Julgar(m, $"{lado}", exigirLimpo: true, contarAcimaDaCabeca: lado != Facing.North);

		Gravar(par.Tela, $"boca-1-{lado.ToString().ToLowerInvariant()}.png",
			   $"CENA A, {lado}: {m.Resumo}");
		if (par.Recorte is { } r) _tiraDosQuatro.Add(r);

		Adiante();
	}

	private void Adiante()
	{
		if (S is { } srv && C is { } cli) srv.LimparOsTirosDaBoca(cli.LocalId);

		if (++_iSentido < Sentidos.Length) { Virar(1); return; }

		if (_tiraDosQuatro.Count > 0)
			Colar("boca-1-os-quatro-sentidos.png", _tiraDosQuatro,
				  "A TIRA: o mesmo corpo atirando pros quatro lados");

		Virar(5);
	}

	// =====================================================================
	// B) O MESMO SENTIDO, COM O DEFEITO INJETADO
	// =====================================================================
	/// <summary>
	/// ============================ SEM ISTO A CENA A NAO VALE NADA ============================
	/// Uma sonda que mede "zero pixel de feixe em cima do corpo" mediria os mesmos zero pixels se
	/// estivesse contando a regiao errada, se a mascara estivesse vazia por engano ou se o recorte
	/// caisse fora da tela. **Injetar o defeito e a unica maneira de ela provar que sabe ficar
	/// vermelha** -- e este projeto ja pagou por acreditar em verde de graca: quatro defeitos visuais
	/// passaram por quatro mil checagens porque a bancada media INTENCAO.
	///
	/// ============================ E O DEFEITO ENTRA NO NASCIMENTO, COM UMA BOLA ============================
	/// A primeira rodada injetava a CAUDA do raio canalizado (`p.Cauda = dono.Pos`, que e a linha exata
	/// de antes do conserto) e a foto saiu IGUAL a boa. O motivo esta no proprio jogo: a cauda de um raio
	/// canalizado e reescrita pelo passo do canal a **cada tique do servidor**, e o tique roda depois da
	/// escrita da bancada e antes do snapshot. A injecao morria dentro do mesmo quadro, sem chegar na tela.
	///
	/// Entao ela entra onde o servidor nao a desfaz: no NASCIMENTO, pelo `deOnde` do `Disparar` -- o
	/// parametro de producao com que a Hellzone nasce em volta do alvo. E o tiro e uma BOLA, fotografada
	/// nos primeiros quadros de vida: e ali que o quadro de 32x32 fica carimbado no lugar de onde ele
	/// nasceu, que e a leitura literal da foto do dono ("saindo DE CIMA do personagem"). O controle e a
	/// MESMA bola, no mesmo rumo, nascendo pela porta normal.
	/// ==================================================================================================
	/// </summary>
	private void B_Atirar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// O CORPO NAO E VIRADO DE NOVO: ele fica no ultimo sentido da cena A, e as duas fotos (controle
		// e injetada) saem NESSE mesmo sentido -- que e o que as torna comparaveis. O rumo de verdade
		// sai do servidor na hora da medida, e nao de um indice guardado aqui.
		srv.RegarOKiDaVariedade(cli.LocalId);
		srv.LimparOsTirosDaBoca(cli.LocalId);

		(_, Facing olhar, _, _) = srv.CorpoDaBoca(cli.LocalId);
		srv.BolaDaBoca(cli.LocalId, MeleeArea.Frente(olhar));
		Virar(6);
	}

	private Image? _fotoBoa;
	private Medida _medidaBoa;

	/// <summary>O obturador da bola dispara QUASE no nascimento -- ver o cabecalho da cena.</summary>
	private const double TilesDaBola = 0.05;

	private void B_Boa(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (!Obturador(mundo, srv, cli, out Par par, alvoDeTiles: TilesDaBola)) return;
		if (par.Falhou) { Conferir(false, $"a cena B teve bola ({par.Porque})"); Virar(8); return; }

		_medidaBoa = Medir(ref par, "boca-2-bola-mascara.png");
		_fotoBoa = par.Recorte;
		Nota($"CENA B (controle, a bola nascendo pela porta normal): {_medidaBoa.Resumo}");

		// A BOLA SE MEDE PELO CENTRO, e nao pela borda -- ver `Medida.CentroAoLongo`.
		Conferir(MathF.Abs(_medidaBoa.CentroAoLongo - ZoneCollision.TileSize) <= 10f,
			$"(controle) a bola de producao nasce com o MIOLO na boca do cano -- centro da tinta a "
			+ $"{_medidaBoa.CentroAoLongo:0.#} px do corpo, contra os {ZoneCollision.TileSize} px de um tile");

		srv.LimparOsTirosDaBoca(cli.LocalId);
		Virar(7);
	}

	private void B_Injetada(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (!_injetouBola)
		{
			if (_t < 0.5) return;
			_injetouBola = true;
			srv.RegarOKiDaVariedade(cli.LocalId);

			(_, Facing olhar, _, _) = srv.CorpoDaBoca(cli.LocalId);
			srv.BolaDaBoca(cli.LocalId, MeleeArea.Frente(olhar),
						   Jandirus.Server.GameServer.DefeitoDaBoca.NoUmbigo);
			_t = 0;
			return;
		}

		if (!Obturador(mundo, srv, cli, out Par par, alvoDeTiles: TilesDaBola)) return;
		if (par.Falhou) { Conferir(false, $"a cena B injetada teve bola ({par.Porque})"); Virar(8); return; }

		Medida m = Medir(ref par, "boca-2-bola-mascara-defeito.png");
		Nota($"CENA B (com o nascimento de volta no umbigo): {m.Resumo}");

		// AS REGRAS DA CENA A, agora ao contrario. Verde aqui quer dizer que elas SABEM reprovar.
		Conferir(MathF.Abs(m.CentroAoLongo) <= 10f,
			$"INJETADO: nascendo no umbigo, o miolo da bola cai EM CIMA do corpo -- centro da tinta a "
			+ $"{m.CentroAoLongo:0.#} px dele, contra {_medidaBoa.CentroAoLongo:0.#} px na producao");
		Conferir(m.NoCorpo > _medidaBoa.NoCorpo * 3,
			$"INJETADO: ...e a regra 'nada de tinta em cima do corpo' REPROVA "
			+ $"({m.NoCorpo} px dentro do quadro do boneco, contra {_medidaBoa.NoCorpo} na producao)");
		Conferir(m.MinAoLongo < _medidaBoa.MinAoLongo - 8f,
			$"INJETADO: ...e a borda da tinta recua pra tras do centro do sprite "
			+ $"({m.MinAoLongo:0.#} px, contra {_medidaBoa.MinAoLongo:0.#} px na producao)");

		if (_fotoBoa != null && par.Recorte != null)
			Colar("boca-2-antes-e-depois.png", [par.Recorte, _fotoBoa],
				  "A DOENCA E O REMEDIO: a esquerda o defeito do dono injetado, a direita a producao");

		srv.LimparOsTirosDaBoca(cli.LocalId);
		Virar(8);
	}

	private bool _injetouBola;

	// =====================================================================
	// C) VOANDO -- o feixe sai da altura do CORPO, e nao do chao
	// =====================================================================
	private void C_Subir(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;

		bool subiu = srv.VoarNaBoca(cli.LocalId, AlturaDaCena);
		Conferir(subiu, $"o corpo levantou voo pelo funil de producao ({AlturaDaCena:0} px de altura)");
		if (!subiu) { Virar(12); return; }

		Virar(9);
	}

	private void C_Atirar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.VoarNaBoca(cli.LocalId, AlturaDaCena);   // ver `C_NoAr`: o corpo desce sozinho enquanto espera
		if (_t < 1.0) return;   // a altura precisa atravessar o fio e o corpo subir na TELA

		srv.RegarOKiDaVariedade(cli.LocalId);
		srv.LimparOsTirosDaBoca(cli.LocalId);
		srv.GatilhoDaVariedade(cli.LocalId, "Kamehameha");
		Virar(10);
	}

	private Image? _fotoNoAr;
	private Medida _medidaNoAr;

	private void C_NoAr(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		// A ALTURA E REAFIRMADA A CADA QUADRO, e nao so no comeco. Medido na segunda rodada: o corpo
		// saiu de 256 px e chegou na foto com 93 -- ele estava DESCENDO enquanto o Kamehameha
		// carregava, e a foto pegou o corpo num degrau e o tiro noutro (o tiro guarda a altura do
		// NASCIMENTO, e esta certo: o dono pousa, o tiro nao). A cena e sobre o desenho do feixe, e nao
		// sobre a fisica da descida -- quem mede subida e queda e a `--diagvoo`.
		srv.VoarNaBoca(cli.LocalId, AlturaDaCena);
		srv.RegarOKiDaVariedade(cli.LocalId);
		if (!Obturador(mundo, srv, cli, out Par par)) return;
		if (par.Falhou) { Conferir(false, $"a cena C teve tiro ({par.Porque})"); Aterrissar(srv, cli); return; }

		_medidaNoAr = Medir(ref par);
		_fotoNoAr = par.Recorte;

		(_, _, float altura, bool voando) = srv.CorpoDaBoca(cli.LocalId);
		Nota($"CENA C: o corpo esta a {altura:0} px do chao (voando={voando}); {_medidaNoAr.Resumo}");

		Conferir(Math.Abs(_medidaNoAr.CentroDy) < 12f,
			$"VOANDO: o feixe e desenhado na ALTURA DO CORPO -- o centro da tinta esta a "
			+ $"{_medidaNoAr.CentroDy:0.#} px do centro do sprite (e nao {altura * Voo.EscalaNaTela:0.#} px "
			+ "abaixo, que e onde o plano do chao ficou)");

		Gravar(par.Tela, "boca-3-voando.png", $"CENA C: atirando no ar -- {_medidaNoAr.Resumo}");
		Virar(11);
	}

	/// <summary>
	/// O DEFEITO DA ALTURA, INJETADO NO NODE: `Altitude = 0` e exatamente o estado de antes -- o campo
	/// existia no servidor, valia pra colisao e **nao chegava ao desenho**.
	/// </summary>
	private void C_Injetada(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.VoarNaBoca(cli.LocalId, AlturaDaCena);
		srv.RegarOKiDaVariedade(cli.LocalId);

		if (NoDoTiro(mundo, srv) is not { } no) { Conferir(false, "a cena C achou o node do tiro"); Aterrissar(srv, cli); return; }

		float guardada = no.Altitude;
		no.Altitude = 0;

		if (!Obturador(mundo, srv, cli, out Par par)) return;
		no.Altitude = guardada;

		if (par.Falhou) { Conferir(false, $"a cena C injetada teve tiro ({par.Porque})"); Aterrissar(srv, cli); return; }

		Medida m = Medir(ref par);
		Nota($"CENA C (com `Altitude = 0` injetado no node): {m.Resumo}");

		Conferir(m.CentroDy > _medidaNoAr.CentroDy + 20f,
			$"INJETADO: sem a altura no desenho o feixe cai pro plano do chao, e a regra REPROVA "
			+ $"({m.CentroDy:0.#} px abaixo do corpo, contra {_medidaNoAr.CentroDy:0.#} px na producao)");

		if (_fotoNoAr != null && par.Recorte != null)
			Colar("boca-3-voando-antes-e-depois.png", [par.Recorte, _fotoNoAr],
				  "VOANDO: a esquerda o feixe no chao (defeito injetado), a direita na altura do corpo");

		Aterrissar(srv, cli);
	}

	private void Aterrissar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		srv.LimparOsTirosDaBoca(cli.LocalId);
		srv.PousarNaBoca(cli.LocalId);
		Virar(12);
	}

	// =====================================================================
	// D) A CAMADA -- o feixe A FRENTE do sprite, com o corpo atras dele
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE O TIRO E PRO NORTE, E POR QUE ELE ATRAVESSA ============================
	/// O node do tiro mora no `Atores`, que ordena por Y. Sem `ZIndex`, quem decide e a Y da CABECA --
	/// que num raio pode estar trinta tiles adiante. **Pro norte a Y da cabeca e MENOR que a de quem
	/// esta no caminho**, ou seja o feixe inteiro desenharia atras dos corpos. E o pior sentido, e por
	/// isso e o sentido desta cena.
	///
	/// `Piercer` (campo de producao da receita) porque o feixe precisa continuar existindo DEPOIS de
	/// encostar na vitima: se ele morre no impacto, nao ha sobreposicao pra fotografar.
	/// ========================================================================================================
	/// </summary>
	private void D_Plantar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.6) return;

		if (_vitima == 0)
		{
			Vec2 norte = MeleeArea.Frente(Facing.North);
			_vitima = srv.VitimaDaBoca(cli.LocalId, norte, tiles: 3, bp: 200_000);
			Conferir(_vitima != 0, "a vitima da cena D entrou no mundo, tres tiles ao norte");
			if (_vitima == 0) { Virar(15); return; }

			srv.ApontarNaBoca(cli.LocalId, Facing.North);
			srv.RaioDaBoca(cli.LocalId, norte, alcanceTiles: 10, piercer: true);
			_t = 0;
			return;
		}

		Conferir(mundo.CorpoDeTeste(_vitima) != null,
			"...e a vitima tem SPRITE na tela (um corpo sem `Visual` nao desenha, e a foto nao teria o que tapar)");
		Virar(13);
	}

	private Image? _fotoNaFrente;
	private int _tintaNaVitimaNaFrente;

	private void D_NaFrente(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (!Obturador(mundo, srv, cli, out Par par, alvoDeTiles: 4.5)) return;
		if (par.Falhou) { Conferir(false, $"a cena D teve tiro ({par.Porque})"); Virar(15); return; }

		_tintaNaVitimaNaFrente = TintaSobre(par, mundo, _vitima);

		// O RECORTE DESTA CENA E EM VOLTA DA VITIMA, e nao do atirador: o que se olha aqui e a
		// sobreposicao -- o corpo com o feixe passando por cima dele.
		if (mundo.PosicaoDesenhadaDe(_vitima) is { } ondeEla) _fotoNaFrente = RecorteEmVolta(par, ondeEla);

		Gravar(par.Tela, "boca-4-camada.png",
			   $"CENA D: o feixe A FRENTE do sprite -- {_tintaNaVitimaNaFrente} px de tinta dentro do quadro da vitima");

		Conferir(_tintaNaVitimaNaFrente > 40,
			$"A CAMADA: o feixe e desenhado POR CIMA do corpo que ele atravessa "
			+ $"({_tintaNaVitimaNaFrente} px de tinta de feixe dentro do quadro de 32x32 da vitima)");

		Virar(14);
	}

	private void D_Injetada(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (NoDoTiro(mundo, srv) is not { } no) { Conferir(false, "a cena D achou o node do tiro"); Virar(15); return; }

		// O DEFEITO: `ZIndex = 0` e o estado de antes -- o node empatado com os corpos dentro do
		// `Atores`, ordenado pela Y da cabeca do feixe.
		int guardado = no.ZIndex;
		no.ZIndex = 0;

		if (!Obturador(mundo, srv, cli, out Par par, alvoDeTiles: 4.5)) return;
		no.ZIndex = guardado;

		if (par.Falhou) { Conferir(false, $"a cena D injetada teve tiro ({par.Porque})"); Virar(15); return; }

		int atras = TintaSobre(par, mundo, _vitima);
		Nota($"CENA D (com `ZIndex = 0` injetado no node): {atras} px de tinta dentro do quadro da vitima");
		if (mundo.PosicaoDesenhadaDe(_vitima) is { } ondeEla) par.Recorte = RecorteEmVolta(par, ondeEla);
		Gravar(par.Tela, "boca-4-camada-defeito.png",
			   $"CENA D, INJETADA: `ZIndex = 0` -- {atras} px de tinta dentro do quadro da vitima");

		// ============================ O CORPO NAO TAPA TUDO, E NAO E PRA TAPAR ============================
		// Medido: 2352 px na producao contra 1176 com o `ZIndex` zerado -- exatamente a METADE. Nao e
		// coincidencia: o que o corpo esconde e o que ele PINTA, e um boneco de 32x32 nao pinta a celula
		// inteira (bracos, pernas e o vao em volta sao transparentes). A tinta que sobra e a que passa
		// pelos buracos do sprite.
		//
		// Entao a regra e de RAZAO e nao de zero: com o feixe na frente ele pinta muito mais dentro do
		// quadro da vitima do que com o feixe atras. Exigir zero seria exigir que o sprite fosse um
		// retangulo cheio.
		// ============================================================================================
		Conferir(atras < _tintaNaVitimaNaFrente * 0.7f,
			$"INJETADO: sem o `ZIndex` o corpo tapa o feixe e a regra da camada REPROVA "
			+ $"({atras} px por cima da vitima, contra {_tintaNaVitimaNaFrente} px na producao -- o corpo "
			+ $"engoliu {_tintaNaVitimaNaFrente - atras} px, que e a parte opaca do sprite)");

		if (_fotoNaFrente != null && par.Recorte != null)
			Colar("boca-4-camada-antes-e-depois.png", [par.Recorte, _fotoNaFrente],
				  "A CAMADA: a esquerda o feixe ATRAS do corpo (defeito injetado), a direita a producao");

		srv.LimparOsTirosDaBoca(cli.LocalId);
		srv.LimparAFoto();
		_vitima = 0;
		Virar(15);
	}

	// =====================================================================
	// E) A COLISAO -- alguem COLADO continua sendo acertado
	// =====================================================================
	/// <summary>
	/// ============================ O RISCO QUE O CONSERTO CRIOU, MEDIDO ============================
	/// O tiro passou a nascer UM TILE a frente. A pergunta obvia e: e quem esta EXATAMENTE nesse tile?
	/// Se o nascimento pulasse por cima dele, o conserto teria trocado um defeito de desenho por um
	/// buraco de combate -- e nenhuma das outras cenas veria isso.
	///
	/// A vitima nasce colada, no tile da propria boca do cano. E o CONTROLE e um tiro que nasce um
	/// tile ALEM dela (`DefeitoDaBoca.DoisTilesAFrente`): ele TEM que errar, senao "acertou" nao esta
	/// medindo nada.
	///
	/// ============================ E O DANO E DE VERDADE, PORQUE O DE BANCADA NAO SE LE ============================
	/// A rodada 4 desta casa imprimiu `vida 100 -> 100` nas DUAS pontas -- na que acertou e na que
	/// errou. Com o dano simbolico das cenas de foto (0,002) a vitima perde 0,0002 de 100, e "acertou
	/// de leve" fica indistinguivel de "nao acertou". Aqui o tiro cobra o dano cheio (o mesmo `BaseDano
	/// = 1` da `--projetilteste`): o que se quer saber e SE encostou, e a resposta tem que ser legivel.
	/// ========================================================================================================
	/// </summary>
	private void E_Plantar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.6) return;

		srv.ApontarNaBoca(cli.LocalId, Facing.East);
		Vec2 leste = MeleeArea.Frente(Facing.East);

		_vitima = srv.VitimaDaBoca(cli.LocalId, leste, tiles: 1, bp: 200_000);
		Conferir(_vitima != 0, "a vitima da cena E nasceu COLADA -- no proprio tile da boca do cano");
		if (_vitima == 0) { Fechar(); return; }

		(_, Vec2 onde, double vida, _) = srv.VitimaDaFoto(_vitima);
		_vidaAntes = vida;
		(Vec2 eu, _, _, _) = srv.CorpoDaBoca(cli.LocalId);
		Nota($"a vitima esta em {onde} com {vida:0.####} de vida; o atirador em {eu}, "
		   + $"e a boca do cano cai em {srv.BocaEsperadaNaBoca(cli.LocalId)}");

		// A BOLA MORRE EM QUEM ACERTA (`piercer` desligado), e e por isso que ela e a prova aqui: o
		// desfecho e categorico, e nao uma casa decimal.
		srv.BolaDaBoca(cli.LocalId, leste, piercer: false, baseDano: 1);
		Virar(16);
	}

	private double _vidaAntes;

	private void E_Acertar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		// O DESFECHO E ANOTADO A CADA QUADRO: um tiro dado em quem esta colado morre no PRIMEIRO tique,
		// e o `TickDosProjeteis` recolhe o cadaver no mesmo tique -- sem esta anotacao a bancada leria
		// "sumiu" tanto pra quem acertou quanto pra quem errou.
		srv.GuardarOFimDaBoca(cli.LocalId);
		(int arrastando, bool vivo, FimDeProjetil fim, double andou) = srv.DesfechoDaBoca(cli.LocalId);

		(bool existe, _, double vida, bool emCombate) = srv.VitimaDaFoto(_vitima);
		bool doeu = existe && vida < _vidaAntes - 1e-6;

		if (!doeu && _t < 4) return;

		if (Tela() is { } tela)
			Gravar(tela, "boca-5-colado.png",
				   $"CENA E: o tiro dado em quem esta COLADO -- vida {_vidaAntes:0.##} -> {vida:0.##}");

		Conferir(doeu,
			$"A COLISAO: o tiro que nasce um tile a frente ACERTA quem esta colado nesse tile "
			+ $"(vida {_vidaAntes:0.##} -> {vida:0.##}; o tiro andou {andou:0.##} tile, fim = {fim}, "
			+ $"vivo = {vivo}, arrastando = {arrastando})");
		Conferir(emCombate, "...e ela entrou em combate pelo funil normal (o agressor foi anotado)");

		srv.LimparOsTirosDaBoca(cli.LocalId);
		Virar(17);
	}

	private void E_Injetada(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;

		if (!_injetouColado)
		{
			_injetouColado = true;

			// ============================ UMA VITIMA NOVA, E ELA E O CONSERTO DA PROPRIA BANCADA ============================
			// A rodada 5 reprovou aqui: o tiro nascido DOIS tiles a frente acertou mesmo assim. Nao era o
			// tiro -- era a vitima. O impacto anterior a EMPURROU pra leste (o `knockback` de sempre), e
			// ela foi parar justamente no tile em que o tiro injetado nasce. A bancada estava medindo o
			// proprio estrago.
			//
			// A vitima velha sai do mundo e outra nasce COLADA de novo, com a vida cheia: e o unico jeito
			// de o controle continuar sendo sobre o NASCIMENTO do tiro.
			// ==========================================================================================================
			srv.LimparAFoto();
			Vec2 leste = MeleeArea.Frente(Facing.East);
			_vitima = srv.VitimaDaBoca(cli.LocalId, leste, tiles: 1, bp: 200_000);
			if (_vitima == 0) { Conferir(false, "nasceu uma vitima nova pro controle da colisao"); Fechar(); return; }

			(_, _, double vida, _) = srv.VitimaDaFoto(_vitima);
			_vidaAntes = vida;

			srv.BolaDaBoca(cli.LocalId, leste,
						   Jandirus.Server.GameServer.DefeitoDaBoca.DoisTilesAFrente,
						   piercer: false, baseDano: 1);
			_t = 0;
			return;
		}

		srv.GuardarOFimDaBoca(cli.LocalId);
		if (_t < 3) return;

		(_, _, double agora, _) = srv.VitimaDaFoto(_vitima);
		Conferir(agora >= _vidaAntes - 1e-6,
			$"INJETADO: nascendo um tile ALEM dela, o mesmo tiro NAO a acerta -- a prova de colisao sabe "
			+ $"reprovar (vida {_vidaAntes:0.####} -> {agora:0.####})");

		srv.LimparOsTirosDaBoca(cli.LocalId);
		srv.LimparAFoto();
		Fechar();
	}

	private bool _injetouColado;

	// =====================================================================
	// O OBTURADOR: O PAR DE FOTOS DO MESMO INSTANTE
	// =====================================================================
	/// <summary>O par: a tela COM o tiro desenhado e a MESMA tela sem ele. Ver o cabecalho da classe.</summary>
	private struct Par
	{
		public Image Tela;         // a foto COM o tiro (a tela inteira, evidencia crua)
		public Image Sem;          // a mesma tela com o node do tiro escondido (a de ANTES)
		public Image Sem2;         // ...e a de DEPOIS. Ver o cabecalho: o fundo tem que estar parado
		public Image? Recorte;     // o recorte de medida, ja ampliado -- pra as tiras
		public Vector2 Corpo;      // onde o corpo foi DESENHADO, em pixel de imagem
		public Vector2 CorpoMundo; // o mesmo ponto, em pixel de mundo -- a ancora das conversoes
		public Vector2 Rumo;       // o rumo do tiro, unitario, em mundo
		public float Escala;       // pixel de imagem por pixel de mundo
		public Rect2I Janela;      // a regiao medida, em pixel de imagem
		public bool Falhou;
		public string Porque;
	}

	private int _obtFase;
	private double _obtEspera;
	private int _obtQuadros;
	private Image? _obtCom, _obtSem;
	private ProjetilDesenhado? _obtNo;

	/// <summary>
	/// ESPERA A CABECA ANDAR E TIRA O PAR. Devolve falso enquanto nao acabou -- quem chama volta no
	/// quadro seguinte.
	///
	/// ============================ A ARVORE E PAUSADA ENTRE AS FOTOS ============================
	/// Sem pausar, a cabeca anda uns dez pixels entre um quadro e o outro e a subtracao das fotos
	/// traria o feixe DUAS vezes -- uma positiva e uma negativa --, o que inflaria a mascara e, pior,
	/// poria tinta fantasma em lugares por onde o feixe so PASSOU. Pausada, o que muda entre as fotos
	/// e o `Visible` de um node (e o que o renderizador anima sozinho, que a terceira foto peneira --
	/// ver o cabecalho da classe).
	///
	/// Esta bancada roda em `ProcessModeEnum.Always`, senao ela congelaria junto e a pausa seria eterna.
	/// ==========================================================================================
	/// </summary>
	private bool Obturador(World mundo, Jandirus.Server.GameServer srv, GameClient cli, out Par par,
						   double alvoDeTiles = TilesDoObturador)
	{
		par = default;

		switch (_obtFase)
		{
			case 0:
			{
				_obtEspera += GetProcessDeltaTime();
				(bool vivo, _, _, _, double andou, _) = srv.TiroDaBoca(cli.LocalId);

				// ============================ O NODE TEM QUE EXISTIR, E NAO SO O TIRO ============================
				// A primeira rodada da cena B saiu com `NENHUMA tinta no recorte`: a bola dispara o
				// obturador com 0,05 tile andado, ou seja no primeiro tique -- ANTES de o anuncio de
				// nascimento atravessar o fio e o `AoNascerTiro` criar o node. Sem node nao ha o que
				// esconder, as tres fotos saem identicas e a mascara nasce vazia. Com o raio isso nunca
				// apareceu porque ele leva 1,8 s de carga antes de existir.
				// ==========================================================================================
				bool desenhado = vivo && NoDoTiro(mundo, srv) != null;
				if (desenhado && andou >= alvoDeTiles) { _obtFase = 1; _obtQuadros = 0; return false; }
				if (_obtEspera < 12) return false;

				par.Falhou = true;
				par.Porque = $"vivo={vivo}, desenhado={desenhado}, andou={andou:0.0} de {alvoDeTiles:0.0} tiles";
				ZerarObturador();
				return true;
			}

			case 1:
				GetTree().Paused = true;
				_obtNo = NoDoTiro(mundo, srv);
				if (_obtNo != null && IsInstanceValid(_obtNo)) _obtNo.Visible = false;
				_obtFase = 2;
				_obtQuadros = 0;
				return false;

			// DOIS QUADROS DE FOLGA A CADA TROCA: `GetImage` devolve o ULTIMO quadro renderizado, e um
			// pedido feito no mesmo quadro da mudanca fotografa o estado de ANTES dela.
			case 2:
				if (_obtQuadros++ < 2) return false;
				_obtSem = Tela();
				if (_obtNo != null && IsInstanceValid(_obtNo)) _obtNo.Visible = true;
				_obtFase = 3;
				_obtQuadros = 0;
				return false;

			case 3:
				if (_obtQuadros++ < 2) return false;
				_obtCom = Tela();
				if (_obtNo != null && IsInstanceValid(_obtNo)) _obtNo.Visible = false;
				_obtFase = 4;
				_obtQuadros = 0;
				return false;

			case 4:
			{
				if (_obtQuadros++ < 2) return false;
				Image? sem2 = Tela();
				if (_obtNo != null && IsInstanceValid(_obtNo)) _obtNo.Visible = true;
				GetTree().Paused = false;

				if (_obtCom == null || _obtSem == null || sem2 == null)
				{
					par.Falhou = true;
					par.Porque = "a janela nao renderizou (headless nao serve pra esta bancada)";
					ZerarObturador();
					return true;
				}

				par.Tela = _obtCom;
				par.Sem = _obtSem;
				par.Sem2 = sem2;
				par.CorpoMundo = mundo.PosicaoDesenhadaDe(cli.LocalId) ?? Vector2.Zero;
				par.Corpo = NaImagem(_obtCom, par.CorpoMundo);
				par.Escala = EscalaDaImagem(_obtCom);

				// ============================ O RUMO SAI DO PROPRIO TIRO, E NAO DO OLHAR ============================
				// Na cena A os dois coincidem (e ha uma checagem so pra cobrar que coincidam). Nas outras
				// nao: a cena D atira pro NORTE por `rumoDado`, com o corpo olhando pra onde parou. Medir
				// "quanto da tinta esta a frente" contra o olhar daria, ali, um numero sobre nada.
				// ===============================================================================================
				(bool vivo, Vec2 cabeca, Vec2 cauda, _, _, _) = srv.TiroDaBoca(cli.LocalId);
				var eixo = new Vector2(cabeca.X - cauda.X, cabeca.Y - cauda.Y);
				if (!vivo || eixo.LengthSquared() < 1f)
				{
					(_, Facing olhar, _, _) = srv.CorpoDaBoca(cli.LocalId);
					Vec2 d = MeleeArea.Frente(olhar);
					eixo = new Vector2(d.X, d.Y);
				}
				par.Rumo = eixo.Normalized();

				if (_obtNo != null && IsInstanceValid(_obtNo)
					&& _obtNo.GetNodeOrNull<Node2D>(LuzDeKi.NomeDoNode) != null)
					Nota("ATENCAO: este tiro tem LUZ pendurada (nao e meio-dia) -- a mascara mede tinta E luz");

				ZerarObturador();
				return true;
			}
		}

		return false;
	}

	private void ZerarObturador()
	{
		_obtFase = 0;
		_obtEspera = 0;
		_obtQuadros = 0;
		_obtCom = null;
		_obtSem = null;
		_obtNo = null;
		if (GetTree() is { Paused: true } t) t.Paused = false;
	}

	/// <summary>
	/// O NODE DESTE TIRO, achado pelo NOME que o `World.AoNascerTiro` da a ele.
	///
	/// Pergunta-se a ARVORE e nao ao `World`: o node e o que o pacote de nascimento criou, e e nele que
	/// a bancada injeta os dois defeitos de desenho (a altura e a camada). Um acessador novo no `World`
	/// so pra isto seria uma porta de bancada dentro do jogo.
	/// </summary>
	private ProjetilDesenhado? NoDoTiro(World mundo, Jandirus.Server.GameServer srv)
	{
		if (C is not { } cli) return null;
		(bool vivo, _, _, _, _, _) = srv.TiroDaBoca(cli.LocalId);
		if (!vivo) return null;

		int id = srv.IdDoTiroDaBoca();
		return mundo.FindChild($"Tiro{id}", recursive: true, owned: false) as ProjetilDesenhado;
	}

	// =====================================================================
	// A MEDIDA
	// =====================================================================
	/// <summary>O que a foto respondeu, tudo em pixel de MUNDO e relativo ao centro do sprite.</summary>
	private struct Medida
	{
		public int Pixels;            // quanta tinta de feixe ha no recorte
		public int Atras;             // ...dela, quanta esta ATRAS do corpo (projecao negativa)
		public int NoCorpo;           // ...quanta cai dentro do quadro de 32x32 do boneco
		public int AcimaDaCabeca;     // ...quanta cai na coluna do boneco, acima da cabeca dele
		public float MinAoLongo;      // a BOCA: a tinta mais proxima, projetada no rumo do olhar
		public float TravessaNaBoca;  // ...e quanto ela desvia pro lado, nessa mesma tinta
		public float CentroDy;        // o centro da tinta, em Y, contra o centro do sprite

		/// <summary>
		/// O CENTRO DA TINTA projetado no rumo -- "a que distancia do corpo esta o MIOLO do tiro".
		///
		/// E a medida certa pra uma BOLA, e a borda nao serve la: a primitiva de emergencia (o desenho
		/// de quem nao tem folha) e um circulo de raio meio tile MAIS um halo, ou seja ela e maior que a
		/// celula por construcao e encosta no corpo mesmo nascendo na boca certa. Medindo o centro, a
		/// pergunta volta a ser sobre o NASCIMENTO e nao sobre o tamanho do desenho: 32 px a frente e a
		/// boca do cano, 0 px e o umbigo.
		/// </summary>
		public float CentroAoLongo;
		public string Resumo;
	}

	/// <summary>
	/// A MASCARA E A MEDIDA -- e a mascara e a REGRA DAS TRES FOTOS (ver o cabecalho da classe): um
	/// pixel e tinta de feixe se ele difere das DUAS fotos sem tiro **e** se as duas concordam entre si
	/// naquele ponto. Onde o fundo se mexeu sozinho, ninguem sabe de quem e a tinta, e o pixel sai.
	///
	/// A mascara sai tambem em FOTO (`ver`): uma sonda que conta pixels tem que poder mostrar quais.
	/// </summary>
	private Medida Medir(ref Par par, string? ver = null)
	{
		var m = new Medida { MinAoLongo = float.PositiveInfinity, Resumo = "" };

		int lado = (int)MathF.Round(LadoDaMedidaEmMundo * par.Escala);
		Rect2I j = Janela(par.Tela, par.Corpo, lado);
		par.Janela = j;

		float somaY = 0, somaS = 0;
		Image? pintada = null;
		if (ver != null)
		{
			pintada = par.Tela.GetRegion(j);
			pintada.Convert(Image.Format.Rgba8);
		}

		for (int y = j.Position.Y; y < j.Position.Y + j.Size.Y; y++)
			for (int x = j.Position.X; x < j.Position.X + j.Size.X; x++)
			{
				if (!EhTinta(par, x, y)) continue;

				// A MARCA NA FOTO DA MASCARA: magenta puro, que nao existe em lugar nenhum desta cena.
				pintada?.SetPixel(x - j.Position.X, y - j.Position.Y, new Color(1f, 0f, 1f));

				// DE PIXEL DE IMAGEM PRA PIXEL DE MUNDO, relativo ao centro do sprite desenhado.
				float dx = (x - par.Corpo.X) / par.Escala;
				float dy = (y - par.Corpo.Y) / par.Escala;

				float s = dx * par.Rumo.X + dy * par.Rumo.Y;          // projecao no rumo do olhar
				float t = dx * par.Rumo.Y - dy * par.Rumo.X;          // o que sobra de lado

				m.Pixels++;
				somaY += dy;
				somaS += s;
				if (s < 0) m.Atras++;
				if (MathF.Abs(dx) < DentroDoBoneco && MathF.Abs(dy) < DentroDoBoneco) m.NoCorpo++;
				if (dy < -DentroDoBoneco && MathF.Abs(dx) < DentroDoBoneco) m.AcimaDaCabeca++;
				if (s < m.MinAoLongo) { m.MinAoLongo = s; m.TravessaNaBoca = t; }
			}

		if (pintada != null && ver != null)
			Gravar(Ampliar(pintada, 2), ver, "A MASCARA (magenta = o que a sonda contou como feixe)");

		if (m.Pixels == 0) { m.MinAoLongo = 0; m.Resumo = "NENHUMA tinta de feixe no recorte"; return m; }

		m.CentroDy = somaY / m.Pixels;
		m.CentroAoLongo = somaS / m.Pixels;
		m.Resumo = $"{m.Pixels} px de feixe; boca a {m.MinAoLongo:0.#} px do centro do sprite "
				 + $"(desvio lateral {m.TravessaNaBoca:0.#} px); {m.Atras} px atras do corpo, "
				 + $"{m.NoCorpo} px dentro do boneco, {m.AcimaDaCabeca} px acima da cabeca; "
				 + $"centro da tinta {m.CentroDy:+0.#;-0.#;0} px em Y e {m.CentroAoLongo:0.#} px a frente";

		par.Recorte = RecorteEmVolta(par, par.CorpoMundo);
		return m;
	}

	/// <summary>O recorte de medida em volta de um ponto do mundo, ja ampliado -- pra as tiras.</summary>
	private static Image RecorteEmVolta(Par par, Vector2 pontoMundo)
	{
		int lado = (int)MathF.Round(LadoDaMedidaEmMundo * par.Escala);
		return Ampliar(par.Tela.GetRegion(Janela(par.Tela, NaImagemDe(par, pontoMundo), lado)), 2);
	}

	/// <summary>As tres regras da cena A, ditas com o numero na mao.</summary>
	private void Julgar(Medida m, string lado, bool exigirLimpo, bool contarAcimaDaCabeca)
	{
		Conferir(m.Pixels > 200, $"{lado}: ha feixe desenhado no recorte ({m.Pixels} px de tinta)");

		Conferir(m.MinAoLongo > 0,
			$"{lado}: a boca do cano esta NA FRENTE dele -- a tinta mais proxima fica a "
			+ $"{m.MinAoLongo:0.#} px do centro do sprite, no lado pra onde ele olha");

		Conferir(MathF.Abs(m.TravessaNaBoca) <= MeioCorpo,
			$"{lado}: ...e ela sai pelo MEIO e nao por um canto ({m.TravessaNaBoca:0.#} px de desvio lateral)");

		// A BOCA ENCOSTA NA FRENTE DO SPRITE -- e este e o numero que o dono pediu. A conta do Core diz
		// 16 px (32 px do centro ate a boca, menos a meia celula da arte do pedaco `origin`); os 4 px de
		// folga sao a rasterizacao e as folhas de celula diferente do jogo.
		Conferir(MathF.Abs(m.MinAoLongo - MeioCorpo) <= 4f,
			$"{lado}: ...e ela ENCOSTA na frente do sprite -- {m.MinAoLongo:0.#} px do centro contra os "
			+ $"{MeioCorpo:0} px que a conta do `BocaDeCano` preve (32 px de boca menos meia celula de arte)");

		if (!exigirLimpo) return;

		Conferir(m.NoCorpo == 0,
			$"{lado}: ...e NENHUM pixel de feixe entra no quadro do boneco ({m.NoCorpo} px)");

		if (contarAcimaDaCabeca)
			Conferir(m.AcimaDaCabeca == 0,
				$"{lado}: ...e NENHUM pixel de feixe fica ACIMA DA CABECA dele ({m.AcimaDaCabeca} px) "
				+ "-- que e literalmente a foto que o dono mandou");
		else
			Nota($"{lado}: {m.AcimaDaCabeca} px acima da cabeca -- e o certo: pro NORTE o feixe sobe "
			   + "pela tela por construcao");
	}

	/// <summary>
	/// QUANTA TINTA DE FEIXE CAI DENTRO DO QUADRO DE 32x32 DESTE CORPO -- a medida da CAMADA.
	///
	/// Um pixel so entra aqui se ele MUDOU quando o tiro foi escondido, ou seja se o tiro e quem estava
	/// pintando ali. Com o feixe atras do sprite, o corpo opaco nao deixa a tinta chegar na tela: os
	/// mesmos pixels ficam iguais nas duas fotos e nao contam.
	/// </summary>
	private static int TintaSobre(Par par, World mundo, int id)
	{
		if (mundo.PosicaoDesenhadaDe(id) is not { } onde) return 0;

		Vector2 centro = NaImagemDe(par, onde);
		int meio = (int)MathF.Round(MeioCorpo * par.Escala);
		int n = 0;

		for (int y = (int)centro.Y - meio; y <= (int)centro.Y + meio; y++)
			for (int x = (int)centro.X - meio; x <= (int)centro.X + meio; x++)
			{
				if (x < 0 || y < 0 || x >= par.Tela.GetWidth() || y >= par.Tela.GetHeight()) continue;
				if (EhTinta(par, x, y)) n++;
			}

		return n;
	}

	/// <summary>
	/// ESTE PIXEL E TINTA DAQUELE TIRO? -- a regra das tres fotos, num lugar so (a cena D pergunta a
	/// mesma coisa por outro caminho, e as duas tem que perguntar identicamente).
	/// </summary>
	private static bool EhTinta(Par par, int x, int y)
	{
		Color com = par.Tela.GetPixel(x, y);
		Color sem = par.Sem.GetPixel(x, y);
		Color sem2 = par.Sem2.GetPixel(x, y);

		// O FUNDO TEM QUE ESTAR PARADO. Se as duas fotos sem tiro discordam aqui, alguma coisa deste
		// cenario se mexeu sozinha (agua, balao desbotando, sangue de NPC) e nao da pra dizer de quem e
		// a tinta -- entao ela nao conta. Ver o cabecalho da classe.
		if (Difere(sem, sem2)) return false;

		return Difere(com, sem) && Difere(com, sem2);
	}

	private static bool Difere(Color p, Color q)
		=> MathF.Abs(p.R - q.R) > Epsilon || MathF.Abs(p.G - q.G) > Epsilon || MathF.Abs(p.B - q.B) > Epsilon;

	// =====================================================================
	// AS FERRAMENTAS DE TELA
	// =====================================================================
	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;
		img.Convert(Image.Format.Rgba8);
		return img;
	}

	/// <summary>
	/// DE PONTO DO MUNDO PRA PIXEL DA IMAGEM.
	///
	/// A `CanvasTransform` leva o mundo pra coordenada de VIEWPORT; a imagem pode ter outro tamanho
	/// (janela redimensionada, `stretch`). A razao entre os dois fecha a conta -- sem ela a medida
	/// inteira sairia escalada por um fator que ninguem veria.
	/// </summary>
	private Vector2 NaImagem(Image img, Vector2 mundo)
	{
		Vector2 v = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;
		Vector2 tam = GetViewport()?.GetVisibleRect().Size ?? img.GetSize();
		return new Vector2(v.X * img.GetWidth() / tam.X, v.Y * img.GetHeight() / tam.Y);
	}

	/// <summary>A mesma conta do <see cref="NaImagem"/>, ja com a escala do par medida.</summary>
	private static Vector2 NaImagemDe(Par par, Vector2 mundo)
	{
		// O par ja sabe onde o CORPO caiu na imagem e quantos pixels de imagem vale um pixel de mundo;
		// tudo o mais e uma soma. Refazer a `CanvasTransform` aqui leria a camera de AGORA -- que ja
		// andou, porque a arvore foi despausada.
		Vector2 corpoMundo = par.CorpoMundo;
		return par.Corpo + (mundo - corpoMundo) * par.Escala;
	}

	/// <summary>QUANTOS PIXELS DE IMAGEM VALE UM PIXEL DE MUNDO -- medido, e nao lido do `Zoom`.</summary>
	private float EscalaDaImagem(Image img)
	{
		Vector2 a = NaImagem(img, Vector2.Zero);
		Vector2 b = NaImagem(img, new Vector2(ZoneCollision.TileSize, 0));
		float e = (b - a).Length() / ZoneCollision.TileSize;
		return e > 0.01f ? e : 1f;
	}

	private static Rect2I Janela(Image img, Vector2 centro, int lado)
	{
		lado = Math.Min(lado, Math.Min(img.GetWidth(), img.GetHeight()));
		int x0 = Math.Clamp((int)centro.X - lado / 2, 0, img.GetWidth() - lado);
		int y0 = Math.Clamp((int)centro.Y - lado / 2, 0, img.GetHeight() - lado);
		return new Rect2I(x0, y0, lado, lado);
	}

	private static Image Ampliar(Image img, int escala)
	{
		Image copia = img.Duplicate() as Image ?? img;
		copia.Convert(Image.Format.Rgba8);
		copia.Resize(copia.GetWidth() * escala, copia.GetHeight() * escala, Image.Interpolation.Nearest);
		return copia;
	}

	private void Gravar(Image img, string nome, string rotulo)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://" + nome);
			img.SavePng(caminho);
			_passos.Add($"  foto   {rotulo}: {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	/// <summary>
	/// AS FOTOS LADO A LADO -- e isto e o formato do pedido, nao enfeite: "prove medindo, nos quatro"
	/// e uma leitura de OLHO, e quatro arquivos de 1600x900 obrigam quem le a abrir quatro janelas e
	/// comparar de cabeca. Os originais continuam gravados; um recorte e sempre uma escolha de quem
	/// recortou.
	/// </summary>
	private void Colar(string nome, List<Image> pedacos, string rotulo)
	{
		if (pedacos.Count == 0) return;

		const int Vao = 8;
		int alt = 0, larg = 0;
		foreach (Image p in pedacos) { alt = Math.Max(alt, p.GetHeight()); larg += p.GetWidth() + Vao; }

		Image colagem = Image.CreateEmpty(Math.Max(1, larg - Vao), Math.Max(1, alt), false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));

		int x = 0;
		foreach (Image p in pedacos)
		{
			// O `BlitRect` EXIGE O MESMO FORMATO nos dois lados e CALA quando nao tem -- a primeira
			// tira da bancada irma saiu um retangulo preto sem erro nenhum no log.
			Image c = p.Duplicate() as Image ?? p;
			c.Convert(Image.Format.Rgba8);
			colagem.BlitRect(c, new Rect2I(Vector2I.Zero, c.GetSize()), new Vector2I(x, 0));
			x += c.GetWidth() + Vao;
		}

		Gravar(colagem, nome, rotulo);
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;

		if (GetTree() is { } t) t.Paused = false;
		if (S is { } srv && C is { } cli)
		{
			srv.LimparOsTirosDaBoca(cli.LocalId);
			srv.PousarNaBoca(cli.LocalId);
			srv.LimparAFoto();
		}

		GD.Print("\n[boca] ===== DE ONDE O FEIXE SAI, E EM QUE CAMADA -- FOTOGRAFADO =====");
		foreach (string l in _passos) GD.Print("[boca] " + l);
		GD.Print(_falhas.Count == 0
			? "[boca] ===== TUDO OK ====="
			: $"[boca] ===== {_falhas.Count} FALHA(S) =====\n[boca]   " + string.Join("\n[boca]   ", _falhas));
		GetTree().Quit();
	}
}
