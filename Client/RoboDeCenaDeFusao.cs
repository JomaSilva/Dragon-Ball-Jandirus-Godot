using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA **CINEMATICA DA FUSAO** (`--diagcenafusao`) ============================
/// A etapa anterior entregou quem a fusao E (nome, roupa, cabelo, o vermelho do SSJ4). Esta responde
/// a ultima parte do pedido do dono: **a cena que acontece antes de ela existir**.
///
///     &lt;godot&gt; --headless --path . --diagcenafusao
///
/// ============================ AS QUATRO LINHAS DO PEDIDO, E ONDE CADA UMA E MEDIDA ============================
///   1. *"UM efeito em cima dos dois personagens, atualmente sao 2 e fica estranho"* -> blocos B e C.
///   2. *"ondas de choque e pedras levitando durante"* -> bloco A (o roteiro) e bloco C (a tela).
///   3. *"perto do fim: o personagem da fusao brilhando, corpo completamente BRANCO, so a silhueta"*
///      -> bloco D.
///   4. *"o branco e a claridade somem lentamente e a cena acaba com a fusao feita"* -> blocos C e D.
///
/// E o item que o dono cobrou como REGRA e nao como desenho -- *"a fusao so EXISTE no fim da cena"* --
/// tem duas metades: a do CLIENTE (o branco e o clarao caem no beat que assume, e nao antes) esta no
/// bloco A; a do SERVIDOR (o `Fundir` so roda na virada, e a cena interrompida nao deixa meio-corpo)
/// nao cabe aqui e tem bancada propria -- `--cenafusaoteste`, em `GameServer.CenaDeFusaoTeste.cs`.
///
/// ============================ O QUE ELA MEDE, BLOCO A BLOCO ============================
///   A. **O ROTEIRO** -- puro Core, sem node nenhum. E o unico bloco que pode afirmar coisas sobre
///      *quando* os efeitos caem, porque e o unico que ve a lista de beats inteira.
///   B. **A ARTE EXISTE E RESOLVE** -- `FusionLight.tres` e `fusion.wav`, pelo banco de recursos
///      IMPORTADOS e nao pela pasta. Este projeto ja perdeu tempo tres vezes com arte que estava no
///      disco e que o Godot nunca importou (as 229 folhas de beam, os 35 atlas, os dois sons divinos).
///   C. **O TOCADOR** -- a cena roda no relogio da bancada, com DOIS corpos de verdade, e a luz e
///      medida no NODE: quantas nasceram (UMA), onde esta (o ponto MEDIO), de que tamanho (o dobro
///      do quadro, o `transform *= 2` do DM), quantos estouros deu e quando ela esta APAGADA.
///   D. **O BRANCO** -- medido no MATERIAL (`LavagemDeTeste`), que e o que o shader vai desenhar, e
///      nao no campo que pediu. Ver a memoria deste projeto sobre *"a bancada mede INTENCAO"*.
///   E. **AS INJECOES** -- cada regra recebe o defeito que ela existe pra pegar e TEM que ficar
///      vermelha. Regra que passa verde com o proprio defeito e falha DA BANCADA.
///
/// ============================ O QUE ELA **NAO** PROVA ============================
/// Headless nao desenha: nenhuma prova daqui e uma foto. O que ela mede sao **os nodes e os uniforms
/// que o caminho de producao escreveu** -- a luz existe, esta no lugar certo, no tamanho certo; o
/// corpo esta com `flash = 1,0` e `flash_cor` branco. Que a GPU pinte aquilo e o que o shader ja faz
/// pro soco e pro banho da transformacao ha meses.
///
/// A UNICA coisa que ela nao alcanca e o Z: `ZIndex = 95` absoluto esta escrito e conferido, mas
/// "por cima" de verdade so a tela responde. Anotado como buraco conhecido, e nao escondido.
/// ==========================================================================================
/// </summary>
public partial class RoboDeCenaDeFusao : Node
{
	private int _ok, _falhou;

	private void Conferir(bool passou, string oque)
	{
		if (passou) { _ok++; GD.Print($"[cenafusao]  ok   {oque}"); }
		else { _falhou++; GD.Print($"[cenafusao]  FALHOU  {oque}"); }
	}

	public override void _Ready()
	{
		GD.Print("[cenafusao] ==== A CINEMATICA DA FUSAO: a luz, as ondas, a pedra e o branco ====");

		ORoteiro();
		AArte();

		VisualCatalog? cat = CarregarCatalogo();
		if (cat == null)
			GD.Print("[cenafusao] sem visual.json -- os blocos C, D e E nao tem corpo pra medir");
		else
		{
			OTocador(cat);
			OBranco(cat);
			AInjecao(cat);
		}

		GD.Print(_falhou == 0
			? $"[cenafusao] ===== TUDO OK ({_ok} provas) ====="
			: $"[cenafusao] ===== {_falhou} FALHA(S) em {_ok + _falhou} provas =====");
		GetTree().Quit(_falhou == 0 ? 0 : 1);
	}

	private static VisualCatalog? CarregarCatalogo()
	{
		const string dados = "res://Assets/Data/visual.json";
		return Godot.FileAccess.FileExists(dados)
			? VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados))
			: null;
	}

	// =====================================================================
	// A. O ROTEIRO -- puro Core
	// =====================================================================
	/// <summary>
	/// O beat que vira, achado pelo mesmo caminho que o servidor usa. Se ele mudar de lugar, TODAS as
	/// contas deste bloco mudam junto -- que e o ponto de a bancada perguntar em vez de cravar 4,0.
	/// </summary>
	private static Beat BeatDaVirada(Cinematica c)
	{
		foreach (Beat b in c.Beats) if (b.Faz.HasFlag(Efeito.Assumir)) return b;
		return default;
	}

	private static int Quantos(Cinematica c, Efeito e)
	{
		int n = 0;
		foreach (Beat b in c.Beats) if (b.Faz.HasFlag(e)) n++;
		return n;
	}

	private void ORoteiro()
	{
		GD.Print("[cenafusao] -- A. o roteiro --");
		Cinematica c = Cinematicas.Fusao;

		// --- A1-A4: a estrutura, e ela e o que sustenta todo o resto ---
		Conferir(Quantos(c, Efeito.Assumir) == 1,
				 $"A1 a cena vira UMA vez ({Quantos(c, Efeito.Assumir)} beats com `Assumir`)");

		// ============================ A VIRADA E O FIM DA ANIMACAO, E NAO UM PRAZO ============================
		// O dono: *"essa animacao do fusion light so toca uma vez, e QUANDO ELA ACABAR a fusao ja vai
		// estar pronta"*. Ou seja o marco temporal da cena deixou de ser um numero (os 4,0 s do
		// `sleep(40)` do `Fusion.dm:678`) e passou a ser um FATO: a duracao da folha.
		//
		// Esta prova le a CONSTANTE do Core; quem confere que a constante bate com a folha IMPORTADA e a
		// B5b, no bloco B. Sao as duas metades: aqui, que o roteiro pendura a virada nela; la, que ela
		// continua sendo o que o `.dmi` diz.
		// ==================================================================================================
		Conferir(Math.Abs(c.SegundosAteAVirada - Cinematicas.SegundosDaLuzDaFusao) < 1e-9,
				 $"A2 a virada cai no FIM DA ANIMACAO DA LUZ ({c.SegundosAteAVirada:0.###}s contra "
			   + $"{Cinematicas.SegundosDaLuzDaFusao:0.###}s de folha) -- e nao num prazo escrito a mao");

		// ============================ A CAUDA E MEDIDA DE DOIS JEITOS, E NENHUM E `< Segundos` ============================
		// `SegundosAteAVirada < Segundos` seria a conta obvia e ela NAO PODE FICAR VERMELHA: `Segundos`
		// e "o ultimo beat + 1,0", entao toda cena termina pelo menos um segundo depois do seu ultimo
		// beat, inclusive uma cena cujo ultimo beat E a virada. Uma prova que nao tem como falhar nao e
		// uma prova -- e foi a injecao E2 que mostrou isso, ficando verde quando devia estar vermelha.
		//
		// Entao sao duas perguntas de verdade: (1) HA beat depois da virada -- a cauda que o roteiro
		// declara --, e (2) ela dura mais que o banho de cor da transformacao, que e o outro gesto do
		// jogo no mesmo canal. A segunda e o que da sentido a palavra *"lentamente"*, e ela e DERIVADA
		// (`Cinematicas.SegundosDoBanho`, os 1,2 s do `spawn(12)` do DM) em vez de um numero escolhido.
		// ============================================================================================================
		bool temBeatDepois = false;
		foreach (Beat b in c.Beats) if (b.Em > c.SegundosAteAVirada) temBeatDepois = true;
		Conferir(c.SegundosAteAVirada > 0 && temBeatDepois,
				 $"A3 HA BEAT depois da virada -- a cauda que o roteiro declara "
			   + $"(vira em {c.SegundosAteAVirada:0.#}s, acaba em {c.Segundos:0.#}s)");

		double cauda = c.Segundos - c.SegundosAteAVirada;
		Conferir(cauda > Cinematicas.SegundosDoBanho,
				 $"A3b a cauda dura MAIS que o banho de cor da transformacao ({cauda:0.#}s contra "
			   + $"{Cinematicas.SegundosDoBanho:0.#}s) -- e o que faz o branco *sumir lentamente*");

		// O CORPO FICA PRESO A CENA INTEIRA: soltar na virada deixaria a fusao correndo pelo mapa
		// como um vulto branco, no unico instante em que ela e um vulto branco.
		Conferir(Math.Abs(c.SegundosPreso - c.Segundos) < 1e-9,
				 $"A4 o corpo fica preso a cena INTEIRA ({c.SegundosPreso:0.#}s de {c.Segundos:0.#}s)");

		// --- A5-A7: os tres efeitos do pedido, e ONDE cada um cai ---
		// ============================ A LUZ TOCA **UMA VEZ**, NO INSTANTE ZERO ============================
		// O `Play()` do `obj/vfx/fusion` do DU chama `flick` TRES vezes (`Code/Misc/_vfx.dm:8-16`), e o
		// dono cortou pra um: *"essa animacao do fusion light so toca uma vez"*. A razao da divergencia
		// esta escrita em `Efeito.LuzDaFusao` -- no DU a luz acende DEPOIS de a fusao existir, aqui ela e
		// a transicao por baixo da qual os dois viram um.
		//
		// O port original tocava a animacao em LACO os 7 segundos (dez piscadas por segundo), e isso era
		// metade do *"fica estranho"* que o dono cobrou; a outra metade eram as duas copias (ver C1).
		// =============================================================================================
		int estouros = Quantos(c, Efeito.LuzDaFusao);
		Conferir(estouros == 1 && c.Beats[0].Faz.HasFlag(Efeito.LuzDaFusao)
				 && Math.Abs(c.Beats[0].Em) < 1e-9,
				 $"A5 a LUZ toca UMA VEZ, no instante ZERO ({estouros} estouro(s)) -- *\"so toca uma vez\"*");

		// ============================ E ELE NAO ALCANCA O CLIMAX ============================
		// A folha e OPACA (alfa binario, 0 ou 255 -- medido nos 147.456 pixels) e na escala 2 do DM ela
		// tapa os dois corpos inteiros. Tapar e o PEDIDO enquanto a transicao acontece (*"cobrindo
		// ambos"*), e deixar de tapar e o pedido a partir da virada -- e o dono quer ver a fusao pronta
		// aparecer. Um beat de luz NA virada (ou depois) esconderia justamente o corpo branco.
		// ==================================================================================
		bool luzNoClimax = false;
		foreach (Beat b in c.Beats)
			if (b.Em >= c.SegundosAteAVirada && b.Faz.HasFlag(Efeito.LuzDaFusao)) luzNoClimax = true;
		Conferir(!luzNoClimax,
				 "A5b nenhum estouro cai NA virada nem depois -- dali em diante quem esta na tela e o "
			   + "corpo branco, e a folha e opaca");

		Beat virada = BeatDaVirada(c);
		Conferir(Quantos(c, Efeito.SilhuetaBranca) == 1 && virada.Faz.HasFlag(Efeito.SilhuetaBranca),
				 "A6 o BRANCO cai EXATAMENTE no beat que assume -- *perto do fim*, e o corpo que ele "
			   + "pinta so existe ali");

		Conferir(Quantos(c, Efeito.ClaraoDeTela) == 1 && virada.Faz.HasFlag(Efeito.ClaraoDeTela),
				 "A7 o CLARAO e um so e mora na virada (a regra do proprio `Efeito.ClaraoDeTela`)");

		// --- A8-A9: "ondas de choque e pedras levitando DURANTE" ---
		Conferir(Quantos(c, Efeito.AnelDeChoque) >= 3,
				 $"A8 ha ONDAS DE CHOQUE ao longo da cena ({Quantos(c, Efeito.AnelDeChoque)} beats)");

		// A PEDRA NAO ESTA EM BEAT NENHUM, de proposito: ela e o estado `OChaoSeSolta`, que corre por
		// baixo da cena inteira. Sem forma no catalogo (`Forma = ""`), `NaoSeSobePraEla` e falso e o
		// chao se solta -- de graca. Esta linha e o que garante que continue assim.
		Conferir(c.OChaoSeSolta,
				 "A9 o CHAO SE SOLTA a cena inteira -- *pedras levitando durante*");

		// ============================ A10: **ESTA CENA NAO ABRE O CHAO**, e isso e do DM ============================
		// Era o avesso: "a cratera e a poeira caem NA virada e nao antes", o funil da `Cinematica.Beats`
		// aplicado a fusao como a toda cena. Duas medidas o derrubaram:
		//
		//   * **o DM** -- `grep createCrater|createDustmisc` nos tres arquivos da fusao do DU
		//     (`_Fusion.dm`, `Metamoran Fusion.dm`, `Potara_Fusion.dm`) nao devolve UMA linha. O
		//     `FusionVFX` cria um `obj/vfx/fusion`, dobra e toca; mais nada;
		//   * **o pixel** -- a `--diagfotofusao` mediu a caixa do boneco no climax e achou **0,0% de
		//     branco**. A fumaca desenha em `ZIndex = 100`, ACIMA do corpo (`Decalques.cs:433`, pedido
		//     do dono pras cenas de forma), e na fusao ela caia no MESMO beat em que o corpo branco
		//     nasce: quatro nuvens (uma da cratera, tres da poeira) em cima da silhueta que esta cena
		//     inteira existe pra mostrar.
		//
		// O roteiro da fusao ja tinha escrito a regra que isso violava, no beat da cauda (*"tapar o
		// corpo branco justamente enquanto ele e o que a cena existe pra mostrar seria o defeito que
		// aposentou o `Efeito.Onda`"*). A regra estava la; quem a violava era o funil, calado.
		//
		// O CHAO CONTINUA REAGINDO: `OChaoSeSolta` (A9) roda a cena inteira e os aneis (A8) tambem.
		// ========================================================================================================
		Conferir(!c.OChaoAbre,
				 "A10 esta cena NAO abre o chao -- o DU nao tem `createCrater` nem `createDustmisc` "
			   + "em nenhum dos tres arquivos da fusao");
		Conferir(c.Beats.All(b => !b.Faz.HasFlag(Efeito.Cratera) && !b.Faz.HasFlag(Efeito.Poeira)),
				 "A10b ...e nenhum beat carrega cratera ou poeira -- a nuvem desenha em `ZIndex = 100`, "
			   + "acima do corpo, e enterrava a silhueta branca (medido: 0,0% de branco no pixel)");

		// --- A11: o som ---
		Conferir(virada.Som == "fusao" && Transformacao.CaminhoDoSom("fusao") is { Length: > 0 },
				 $"A11 a virada toca o `fusion.wav` do DM (`Fusion.dm:679`) e o nome RESOLVE "
			   + $"(`{virada.Som}` -> {Transformacao.CaminhoDoSom(virada.Som)?.GetFile() ?? "NULO"})");

		// --- A12-A14: a cena nao e de forma, e isso tem consequencias ---
		Conferir(!Cinematicas.Todas.Contains(Cinematicas.Fusao),
				 "A12 a cena da fusao fica FORA da `Todas` (fundir nao e uma forma)");

		Conferir(c.Forma.Length == 0 && c.Musica.Length == 0,
				 "A13 sem forma e sem tema -- o tema e marcador de estreia, e fundir se repete");

		Conferir(!c.OCeuDescarrega,
				 "A14 o ceu NAO descarrega (aquilo e da estreia do SSJ1, e so dela)");

		// --- A15: nada que exija forma, senao o beat sairia calado ---
		const Efeito PedemForma = Efeito.VesteDegrau | Efeito.PiscaCabelo | Efeito.Raios
								| Efeito.BanhoDeCor | Efeito.AuraGrande | Efeito.AuraBase;
		bool pediuForma = false;
		foreach (Beat b in c.Beats) if ((b.Faz & PedemForma) != 0) pediuForma = true;
		Conferir(!pediuForma,
				 "A15 nenhum beat pede efeito que dependa de FORMA -- sem forma eles sairiam calados "
			   + "(ou, no caso da chama, VERMELHO SANGUE: ver `Efeito.LuzDaFusao`)");

		// --- A16: a luz da fusao NAO e a silhueta do bio ---
		Conferir(c.Silhueta == FolhaDeSilhueta.Nenhuma && Quantos(c, Efeito.SilhuetaDoCorpo) == 0,
				 "A16 a cena NAO usa a silhueta de camada do bio -- a luz da fusao e outro desenho "
			   + "(128x128 desenhados no dobro, no MEIO dos dois, e nao 32x32 vestindo um)");
	}

	// =====================================================================
	// B. A ARTE -- importada, e nao "esta na pasta"
	// =====================================================================
	private void AArte()
	{
		GD.Print("[cenafusao] -- B. a arte --");

		// ============================ `Exists` E A PERGUNTA CERTA ============================
		// Ela responde "o Godot RESOLVE este caminho?", que e diferente de "o arquivo esta la": um
		// `.tres` sem o `.png` importado (sem `.import`, sem `.ctex` em `.godot/imported/`) carrega
		// NULO e o efeito some calado. Foi exatamente o que aconteceu com os 35 atlas e com as 229
		// folhas de beam deste port.
		// ================================================================================
		bool temFolha = ResourceLoader.Exists(Transformacao.CaminhoDaLuzDaFusao);
		Conferir(temFolha, $"B1 `{Transformacao.CaminhoDaLuzDaFusao.GetFile()}` esta IMPORTADA");

		if (temFolha)
		{
			var f = ResourceLoader.Load<SpriteFrames>(Transformacao.CaminhoDaLuzDaFusao);
			string[] anims = f?.GetAnimationNames() ?? [];
			Conferir(f != null && anims.Length > 0,
					 $"B2 ela carrega como SpriteFrames com animacao ({anims.Length})");

			if (f != null && anims.Length > 0)
			{
				int quadros = f.GetFrameCount(anims[0]);
				Conferir(quadros >= 2, $"B3 a luz ANIMA ({quadros} quadros em `{anims[0]}`)");

				// O TAMANHO E O QUE FAZ ELA COBRIR OS DOIS. Esta linha e o que impede alguem trocar a
				// arte por uma de 32 px e o efeito virar um brilho no umbigo.
				Vector2 tam = f.GetFrameTexture(anims[0], 0)?.GetSize() ?? Vector2.Zero;
				float tiles = tam.X / Jandirus.Core.World.ZoneCollision.TileSize;
				Conferir(tiles >= 2f,
						 $"B4 a luz cobre mais de um corpo ({tam.X:0}x{tam.Y:0} px = {tiles:0.#} tiles)");

				// ============================ E O PIOR CASO ENCOLHEU, PORQUE OS DOIS CHEGAM COLADOS ============================
				// Uma luz SO, no ponto medio, precisa de raio `d/2 + 16` pra pegar os dois corpos (16 e
				// meio boneco), ou seja de um lado desenhado de `d + 32`. O `d` maximo era o portao do
				// convite (4 tiles) e passou a ser o TILE AO LADO: a Danca e a Namekuseijin exigem
				// `Fusao.TilesColados` no convite e no aceite, e a Potara PUXA os dois ate isso valer
				// (`Fusao.PuxaOsCorpos`). O pior caso e o vizinho de DIAGONAL -- 1 tile em cada eixo,
				// 45,25 px entre centros.
				//
				// Com isso a escala 2 do `_Fusion.dm:121` deixou de ser a coisa que faz o efeito
				// funcionar e voltou a ser o que ela e no original: o tamanho do clarao. A prova
				// continua valendo (e o que barra uma arte pequena demais) e agora sobra folga -- que e
				// o certo, porque o dono pediu o clarao *"cobrindo ambos"*.
				// ==========================================================================================================
				float lado = tam.X * Transformacao.EscalaDaLuzDaFusao;
				float precisa = Jandirus.Core.Social.Fusao.TilesColados
							  * Jandirus.Core.World.ZoneCollision.TileSize * Mathf.Sqrt2
							  + Jandirus.Core.World.ZoneCollision.TileSize;
				Conferir(lado >= precisa,
						 $"B4b na escala {Transformacao.EscalaDaLuzDaFusao:0.#}x do DM ela cobre os dois "
					   + $"colados no pior caso, o vizinho de diagonal ({lado:0} px desenhados contra "
					   + $"{precisa:0} px pedidos)");

				// A ESCALA E INTEIRA -- e e isso que a impede de borrar. Ver `EscalaDaLuzDaFusao`: com
				// `Nearest` cada pixel vira um bloco NxN exato; uma escala quebrada daria linhas de
				// larguras diferentes num disco que pulsa dez vezes por segundo.
				Conferir(Math.Abs(Transformacao.EscalaDaLuzDaFusao
								  - Mathf.Round(Transformacao.EscalaDaLuzDaFusao)) < 1e-6f,
						 $"B4c ...e a escala e INTEIRA ({Transformacao.EscalaDaLuzDaFusao:0.###}) -- "
					   + "com `Nearest`, escala inteira nao interpola e nao borra");

				// ============================ E O ESTOURO ACABA **NA** VIRADA -- E ISSO E O PEDIDO ============================
				// A duracao e da FOLHA (soma dos quadros / velocidade) -- o mesmo caminho que o tocador
				// usa. Ela e a metade que o bloco A nao alcanca: la se prova que o roteiro pendura a
				// virada na constante do Core; aqui se prova que o estouro que de fato existe termina
				// naquele instante.
				//
				// *"quando ela acabar a fusao ja vai estar pronta"* -- entao a comparacao e `<=` com
				// tolerancia e nao `<`: o certo e coincidir, e nao sobrar.
				// ==========================================================================================================
				double soma = 0;
				for (int i = 0; i < quadros; i++) soma += f.GetFrameDuration(anims[0], i);
				double vel = f.GetAnimationSpeed(anims[0]);
				double dura = vel > 0 ? soma / vel : 0;

				double ultimo = -1;
				foreach (Beat b in Cinematicas.Fusao.Beats)
					if (b.Faz.HasFlag(Efeito.LuzDaFusao)) ultimo = b.Em;

				Conferir(dura > 0 && ultimo >= 0
						 && ultimo + dura <= Cinematicas.Fusao.SegundosAteAVirada + 1e-6,
						 $"B5 o estouro acaba ATE a virada ({ultimo:0.###} + {dura:0.###} s de "
					   + $"folha = {ultimo + dura:0.###} s, virada em "
					   + $"{Cinematicas.Fusao.SegundosAteAVirada:0.###} s)");

				// ============================ B5b -- A CONSTANTE DO CORE **E** A FOLHA ============================
				// Esta e a prova que fecha o "fato em vez de relogio". `Cinematicas.SegundosDaLuzDaFusao`
				// e um numero declarado no Core (que nao conhece Godot e nao tem como abrir o `.tres`), e o
				// roteiro inteiro pendura a virada nele. Se alguem reconverter o `.dmi` com outra cadencia
				// -- outro `delay`, outro numero de quadros --, a folha e a constante divergem e a cena
				// passa a fundir antes ou depois do clarao **calada**.
				//
				// E exatamente a familia de defeito que este projeto ja catalogou por escrito ("uniform
				// escrito nao e pixel desenhado"): o numero certo no codigo nao prova o desenho certo na
				// tela. Aqui a prova compara os dois lados de verdade.
				// ============================================================================================
				Conferir(Math.Abs(dura - Cinematicas.SegundosDaLuzDaFusao) < 1e-6,
						 $"B5b a constante do Core E a folha ({Cinematicas.SegundosDaLuzDaFusao:0.####} s "
					   + $"declarados contra {dura:0.####} s medidos em {quadros} quadros a {vel:0.#} fps)");
			}
		}

		string? som = Transformacao.CaminhoDoSom("fusao");
		Conferir(som is { Length: > 0 } && ResourceLoader.Exists(som),
				 $"B6 o `fusion.wav` do DM esta IMPORTADO ({som?.GetFile() ?? "NULO"})");
	}

	// =====================================================================
	// O CORPO DE MENTIRA -- dois, porque esta cena tem dois
	// =====================================================================
	/// <summary>
	/// Um corpo com o filho `Visual`, que e como o <see cref="Transformacao"/> acha o boneco. O nome
	/// do filho e "Visual" e nao um qualquer: e o `GetNodeOrNull&lt;CharacterVisual&gt;("Visual")` do
	/// tocador -- errar o nome faria TODA prova do bloco D passar por "nao mediu nada".
	/// </summary>
	private Node2D MontarCorpo(VisualCatalog cat, string nome, Vector2 onde)
	{
		var corpo = new Node2D { Name = nome, Position = onde };
		var vis = new CharacterVisual { Name = "Visual" };
		corpo.AddChild(vis);
		AddChild(corpo);
		vis.Vestir(cat, new Appearance { Cabelo = "Goku" }, "Saiyan", "Male");
		return corpo;
	}

	private static CharacterVisual Vis(Node2D corpo) => corpo.GetNode<CharacterVisual>("Visual");

	/// <summary>
	/// TIRA UM CORPO DO MUNDO **AGORA**, e nao no fim do quadro.
	///
	/// ============================ `QueueFree` NAO SERVE AQUI, E ISSO CUSTOU TRES PROVAS ============================
	/// Ele e ADIADO: o node so morre no fim do quadro da ENGINE, e esta bancada roda a cena inteira
	/// dentro do `_Ready` -- nenhum quadro de engine acontece no meio. Com `QueueFree` o corpo
	/// "removido" continuava valido pra o `IsInstanceValid` do tocador, e as tres provas de
	/// interrupcao mediam um corpo que nunca tinha saido.
	///
	/// **E o jogo nao muda por isso**: la o `QueueFree` do `World.SumirCorpo` de fato invalida o node
	/// no quadro seguinte, e o tocador ve o mesmo estado que esta linha produz -- so que um quadro
	/// depois. O que a bancada precisa e do ESTADO, e nao do atraso.
	/// ==========================================================================================================
	/// </summary>
	private static void SumirAgora(Node2D corpo)
	{
		corpo.GetParent()?.RemoveChild(corpo);
		corpo.Free();
	}

	/// <summary>
	/// RODA A CENA NO RELOGIO DA BANCADA -- `SetProcess(false)` + `_Process(passo)` a mao, como o
	/// resto das bancadas de cena deste projeto. O `oQue` e chamado DEPOIS de cada passo, com o
	/// tempo de cena ja avancado.
	/// </summary>
	/// <param name="boneco">
	/// O `CharacterVisual` do corpo da cena -- e ele tem que ser BOMBEADO JUNTO.
	///
	/// ============================ POR QUE, E O QUE ISSO CUSTOU ============================
	/// O branco escoa no `_Process` do <see cref="CharacterVisual"/> (`_flash -= delta`), e nao no da
	/// cena: sao dois relogios, o da cena manda acender e o do boneco faz apagar. Bombeando so o da
	/// cena, o branco ficava CHAPADO pra sempre -- e as duas provas do escoamento acusavam o jogo por
	/// um defeito da bancada.
	///
	/// **No jogo os dois andam sozinhos, no mesmo `delta` da engine.** Passar o mesmo passo aqui e
	/// reproduzir isso, e nao contornar nada.
	/// ==================================================================================
	/// </param>
	private static void Rodar(Transformacao t, double ate, Action<double> oQue,
							  CharacterVisual? boneco = null, double passo = 0.05)
	{
		for (double x = 0; x < ate && IsInstanceValid(t) && t.Rodando; x += passo)
		{
			t._Process(passo);
			if (boneco != null && IsInstanceValid(boneco)) boneco._Process(passo);
			if (!IsInstanceValid(t)) return;
			oQue(t.TempoDeTeste);
		}
	}

	// =====================================================================
	// C. O TOCADOR -- a UNICA luz, medida no node
	// =====================================================================
	private void OTocador(VisualCatalog cat)
	{
		GD.Print("[cenafusao] -- C. o tocador: UMA luz, no meio dos dois --");
		Cinematica c = Cinematicas.Fusao;

		// --- C1-C4: nasce UMA luz, no ponto medio, no tamanho do DM ---
		Node2D dono = MontarCorpo(cat, "DonoC", Vector2.Zero);
		Node2D irmao = MontarCorpo(cat, "IrmaoC", new Vector2(64, 0));
		var t = Transformacao.Rodar(this, dono, forma: null, c, souEu: true, "bancada", irmao: irmao);
		t.SetProcess(false);
		try
		{
			t._Process(0.01);

			// ============================ O PEDIDO DO DONO, MEDIDO NO NODE ============================
			// *"n e um em cada personagem, e so UM efeito em cima dos dois personagens"*. E e o DM:
			// `FusionVFX` faz UM `new` com UM mob (`_Fusion.dm:120`) -- nao ha laco nem segunda chamada.
			// =====================================================================================
			Conferir(t.LuzesDaFusaoDeTeste == 1,
					 $"C1 nasce UMA luz so -- o `new` unico do `_Fusion.dm:120` ({t.LuzesDaFusaoDeTeste})");

			Vector2 meio = (dono.GlobalPosition + irmao.GlobalPosition) * 0.5f;
			Conferir(t.PosDaLuzDaFusaoDeTeste is { } p1 && p1.DistanceTo(meio) < 1f,
					 $"C2 ela nasce no PONTO MEDIO dos dois "
				   + $"(luz {t.PosDaLuzDaFusaoDeTeste?.ToString() ?? "NULA"}, meio {meio})");

			// ============================ E ELA NAO ESTA EM CIMA DE NENHUM DOS DOIS ============================
			// Sem esta metade, uma luz centrada no `_alvo` passaria a C2 sempre que os dois estivessem
			// no mesmo lugar -- e passaria a C3 tambem, porque o ponto medio anda junto. Mover o irmao
			// pra LONGE e o que separa "no meio" de "em cima de um deles": a 200 px de distancia, o meio
			// esta a 100 px de cada corpo.
			// ==============================================================================================
			irmao.Position = new Vector2(200, 120);
			t._Process(0.01);
			Vector2 meio2 = (dono.GlobalPosition + irmao.GlobalPosition) * 0.5f;
			Vector2? p2 = t.PosDaLuzDaFusaoDeTeste;
			Conferir(p2 is { } q && q.DistanceTo(meio2) < 1f,
					 "C3 ...e ela ACOMPANHA o ponto medio quando um dos dois anda");
			Conferir(p2 is { } q2 && q2.DistanceTo(dono.GlobalPosition) > 1f
					 && q2.DistanceTo(irmao.GlobalPosition) > 1f,
					 "C3b ...e o ponto medio NAO e nenhum dos dois corpos (ela nao esta 'em cima de um')");

			// ============================ O TAMANHO DESENHADO, E NAO A CONSTANTE ============================
			// `O.transform *= 2` (`_Fusion.dm:121`). Ver `Transformacao.TamanhoDaLuzDeTeste`: quem
			// responde e a folha vezes a escala do NODE. Uma prova que relesse a constante ficaria verde
			// no dia em que alguem esquecesse de aplica-la ao sprite.
			// ==========================================================================================
			Vector2? tam = t.TamanhoDaLuzDeTeste;
			// O PIOR CASO E O VIZINHO DE DIAGONAL, e nao mais o portao do convite -- ver a B4b, que
			// explica por que ele encolheu (os dois chegam colados: ou o convite exigiu, ou o puxao
			// fechou).
			float precisa = Jandirus.Core.Social.Fusao.TilesColados
						  * Jandirus.Core.World.ZoneCollision.TileSize * Mathf.Sqrt2
						  + Jandirus.Core.World.ZoneCollision.TileSize;
			Conferir(tam is { } s && s.X >= precisa && s.Y >= precisa,
					 $"C4 ela e desenhada no DOBRO do quadro ({tam?.ToString() ?? "NULA"} px) -- o "
				   + $"bastante pra cobrir os dois colados ({precisa:0} px)");

			// --- C5-C8: UM estouro, e a leitura "clarao cobrindo -> corpo branco" ---
			// ============================ O QUE ESTA SENDO MEDIDO AQUI ============================
			// Nao e "a luz esta acesa" nem "a luz sumiu": e a SEQUENCIA que o dono descreveu -- o clarao
			// cobre os dois, ele acaba, e o que aparece dali em diante e a fusao branca. A trilha guarda
			// (instante, acesa) e as tres provas seguintes leem dela.
			// ===================================================================================
			var trilhaDaLuz = new List<(double T, bool Acesa)>();
			Rodar(t, c.Segundos + 2.0, agora => trilhaDaLuz.Add((agora, t.LuzDaFusaoAcesaDeTeste)));

			Conferir(t.EstourosDaLuzDeTeste == Quantos(c, Efeito.LuzDaFusao),
					 $"C5 a cena disparou UM estouro por beat de luz ({t.EstourosDaLuzDeTeste} de "
				   + $"{Quantos(c, Efeito.LuzDaFusao)}) -- e o roteiro tem UM");

			// ============================ ELA COBRE, E DEPOIS SAI DA FRENTE ============================
			// As duas metades numa prova so, porque uma sem a outra e o defeito: acesa a cena inteira e
			// a cortina que o dono chamou de estranha; nunca acesa e o clarao que nao cobre nada. Com um
			// estouro de 0,7 s numa cena de 3,7 a fracao certa e ~19%.
			// ======================================================================================
			int descidas = 0, acesas = 0;
			for (int i = 1; i < trilhaDaLuz.Count; i++)
				if (!trilhaDaLuz[i].Acesa && trilhaDaLuz[i - 1].Acesa) descidas++;
			foreach ((double T, bool Acesa) a in trilhaDaLuz) if (a.Acesa) acesas++;
			double fracao = trilhaDaLuz.Count == 0 ? 1 : (double)acesas / trilhaDaLuz.Count;

			Conferir(descidas == 1 && acesas > 0 && fracao < 0.5,
					 $"C6 ...e ela ACENDE e APAGA uma vez ({descidas} apagamento(s)), ficando acesa em "
				   + $"{fracao * 100:0}% da cena -- e clarao, e nao cortina");

			// ============================ O CLARAO COBRE **DESDE O COMECO** ============================
			// *"a `FusionLight.png` aparece entre eles cobrindo ambos"*. Antes da virada a tela tem que
			// ter disco: se ela estivesse limpa ali, os dois corpos apareceriam nus no instante em que a
			// cena existe pra fundi-los, e a troca de dois bonecos por um seria um corte seco.
			//
			// (Esta prova era o INVERSO ate este passe -- ela exigia uma *janela limpa* entre os tres
			// estouros do DU. Com o pedido do dono de tocar uma vez so, a janela limpa deixou de existir
			// de proposito: quem mostra os dois corpos separados agora e o PUXAO, que acontece ANTES da
			// cena e nao dentro dela.)
			// ======================================================================================
			bool cobriuDesdeOComeco = true;
			int amostrasAntes = 0;
			foreach ((double T, bool Acesa) a in trilhaDaLuz)
			{
				if (a.T >= c.SegundosAteAVirada) continue;
				amostrasAntes++;
				if (!a.Acesa) cobriuDesdeOComeco = false;
			}
			Conferir(amostrasAntes > 0 && cobriuDesdeOComeco,
					 $"C7 o clarao esta na tela em TODA a janela antes da virada ({amostrasAntes} "
				   + "amostras) -- *\"aparece entre eles cobrindo ambos\"*");

			// E NO CLIMAX A TELA ESTA LIMPA. A metade do bloco A prova que nenhum beat de luz cai la;
			// esta prova o TOCADOR: o estouro acaba NO beat da virada, por construcao (o
			// `Efeito.Assumir` chama o `TerminarOEstouroDaLuz` antes do `Assumir`) -- e nao um quadro
			// depois, que era o que o relogio sozinho entregava.
			bool acesaNaVirada = false;
			foreach ((double T, bool Acesa) a in trilhaDaLuz)
				if (a.Acesa && a.T >= c.SegundosAteAVirada) acesaNaVirada = true;
			Conferir(!acesaNaVirada,
					 "C8 na virada e depois dela NAO ha disco nenhum na frente -- o corpo branco aparece");

			// --- C9: nao sobra luz nenhuma ---
			Conferir(t.FimDeTeste == Transformacao.FimDaCena.Sozinha,
					 $"C9 a cena acabou SOZINHA, sem o teto ({t.FimDeTeste})");
		}
		finally
		{
			if (IsInstanceValid(t)) t.QueueFree();
			dono.QueueFree();
			irmao.QueueFree();
		}

		OSegundoCorpoPodeFaltar(cat);
		AInterrupcao(cat);
	}

	/// <summary>
	/// SEM SEGUNDO CORPO A CENA RODA IGUAL, e a luz fica em cima do unico que ha.
	///
	/// Nao e zelo: e o caso normal de quem esta com o boneco do passageiro ainda por nascer (o
	/// snapshot chega por outro canal) ou fora da vista. Cancelar a cena por causa disso deixaria a
	/// fusao acontecer sem cinematica nenhuma -- e o servidor vai fundir de todo jeito.
	///
	/// **E este e o caminho BOM depois da virada tambem**: o passageiro vai pro selo e o ponto medio
	/// passa a ser o corpo do dono sozinho. Ver `Transformacao.PontoMedioDaLuz`.
	/// </summary>
	private void OSegundoCorpoPodeFaltar(VisualCatalog cat)
	{
		Node2D dono = MontarCorpo(cat, "DonoSozinho", new Vector2(300, 40));
		var t = Transformacao.Rodar(this, dono, forma: null, Cinematicas.Fusao, souEu: true,
									"bancada", irmao: null);
		t.SetProcess(false);
		try
		{
			t._Process(0.01);
			Conferir(t.LuzesDaFusaoDeTeste == 1
					 && t.PosDaLuzDaFusaoDeTeste is { } p
					 && p.DistanceTo(dono.GlobalPosition) < 1f,
					 $"C10 sem segundo corpo a cena roda com UMA luz, em cima do unico corpo "
				   + $"({t.LuzesDaFusaoDeTeste}, luz {t.PosDaLuzDaFusaoDeTeste?.ToString() ?? "NULA"}, "
				   + $"corpo {dono.GlobalPosition})");

			Rodar(t, Cinematicas.Fusao.Segundos + 2.0, _ => { });
			Conferir(!IsInstanceValid(t) || t.FimDeTeste == Transformacao.FimDaCena.Sozinha,
					 "C11 ...e ela chega ao fim sozinha do mesmo jeito");
		}
		finally
		{
			if (IsInstanceValid(t)) t.QueueFree();
			dono.QueueFree();
		}
	}

	/// <summary>
	/// ============================ A CENA INTERROMPIDA -- O ITEM 4 DO PEDIDO ============================
	/// *"a cena interrompida (nocaute, morte, zona, logout de um dos dois) tem que desfazer sem deixar
	/// meio-corpo nem cena presa"*.
	///
	/// Do lado do CLIENTE ha dois jeitos de ela ser interrompida, e os dois estao aqui:
	///
	///   * **o segundo corpo some** -- e este e o caminho BOM tambem, e nao so o de erro: na virada o
	///     servidor manda o passageiro pro selo e o boneco dele deixa a zona. A cena **nao pode**
	///     morrer por isso;
	///   * **o corpo da cena some** (o dono morreu, mudou de zona, deslogou) -- ai a cena morre, e o
	///     que ela nao pode e deixar o corpo preso nem a luz pendurada.
	///
	/// A metade do SERVIDOR (o `Fundir` que nao roda, os dois soltos, nada aplicado) e a
	/// `--cenafusaoteste`.
	/// ============================================================================================
	/// </summary>
	private void AInterrupcao(VisualCatalog cat)
	{
		// --- C12-C13: o segundo corpo some no meio, e a cena continua ---
		{
			// LONGE DA ORIGEM de proposito: com o dono em (0,0) o ponto medio de "so ele" e o mesmo
			// (0,0) de qualquer coisa que der errado, e a C13 ficaria verde por acaso. Aqui o meio dos
			// dois e (152,60) e o corpo do dono e (120,60) -- sao pontos DIFERENTES.
			Node2D dono = MontarCorpo(cat, "DonoI", new Vector2(120, 60));
			Node2D irmao = MontarCorpo(cat, "IrmaoI", new Vector2(184, 60));
			var t = Transformacao.Rodar(this, dono, forma: null, Cinematicas.Fusao, souEu: true,
										"bancada", irmao: irmao);
			t.SetProcess(false);
			try
			{
				t._Process(0.01);
				SumirAgora(irmao);               // o passageiro deixa a zona
				t._Process(0.05);

				Conferir(IsInstanceValid(t) && t.Rodando,
						 "C12 o segundo corpo sumindo NAO derruba a cena (e o caminho normal da virada)");

				// A LUZ NAO MORRE COM ELE -- ela ANDA. Com o passageiro fora da zona o ponto medio vira
				// o corpo do dono sozinho, que e onde a fusao acabou de nascer. (Com duas luzes, a do
				// irmao morria aqui; com uma so, o que muda e a ancora.)
				Conferir(t.LuzesDaFusaoDeTeste == 1
						 && t.PosDaLuzDaFusaoDeTeste is { } p
						 && p.DistanceTo(dono.GlobalPosition) < 1f,
						 $"C13 ...e a luz passa a ficar sobre o corpo que sobrou "
					   + $"({t.PosDaLuzDaFusaoDeTeste?.ToString() ?? "NULA"} contra {dono.GlobalPosition})");
			}
			finally { if (IsInstanceValid(t)) t.QueueFree(); SumirAgora(dono); }
		}

		// --- C14-C16: o corpo da cena some, e a cena se desfaz inteira ---
		{
			Node2D dono = MontarCorpo(cat, "DonoM", Vector2.Zero);
			Node2D irmao = MontarCorpo(cat, "IrmaoM", new Vector2(64, 0));
			var t = Transformacao.Rodar(this, dono, forma: null, Cinematicas.Fusao, souEu: true,
										"bancada", irmao: irmao);
			t.SetProcess(false);

			t._Process(0.01);
			Conferir(Transformacao.PrendendoOCorpo, "C14 a cena PRENDE o corpo ao comecar");

			int presosAntes = Transformacao.PresosDeTeste;
			SumirAgora(dono);                    // o dono morreu / deslogou / trocou de zona
			t._Process(0.05);

			Conferir(!IsInstanceValid(t) || t.FimDeTeste == Transformacao.FimDaCena.AlvoSumiu,
					 "C15 o corpo da cena sumindo encerra a cena por `AlvoSumiu`");
			Conferir(Transformacao.PresosDeTeste < presosAntes,
					 $"C16 ...e o corpo e DEVOLVIDO (presos {presosAntes} -> "
				   + $"{Transformacao.PresosDeTeste}) -- ninguem fica travado");

			if (IsInstanceValid(t)) t.QueueFree();
			SumirAgora(irmao);
		}
	}

	// =====================================================================
	// D. O BRANCO -- medido no material
	// =====================================================================
	private void OBranco(VisualCatalog cat)
	{
		GD.Print("[cenafusao] -- D. o branco --");
		Cinematica c = Cinematicas.Fusao;

		Node2D dono = MontarCorpo(cat, "DonoD", Vector2.Zero);
		Node2D irmao = MontarCorpo(cat, "IrmaoD", new Vector2(64, 0));
		CharacterVisual vis = Vis(dono);
		var t = Transformacao.Rodar(this, dono, forma: null, c, souEu: true, "bancada", irmao: irmao);
		t.SetProcess(false);
		try
		{
			Conferir(vis.LavagemDeTeste != null,
					 "D1 o boneco tem camada com shader -- senao TODO este bloco mede o nada");
			if (vis.LavagemDeTeste == null) return;

			t._Process(0.01);
			Conferir(vis.LavagemDeTeste!.Value.Mistura < 0.01f,
					 $"D2 ANTES da virada o corpo nao esta lavado "
				   + $"({vis.LavagemDeTeste!.Value.Mistura:0.###})");

			// --- corre ate a virada e mede o pico ---
			(float Mistura, Color Cor, float Achatar) noPico = default;
			bool pegou = false;
			var trilha = new List<(double T, float M)>();
			Rodar(t, c.Segundos + 2.0, agora =>
			{
				if (vis.LavagemDeTeste is not { } l) return;
				trilha.Add((agora, l.Mistura));
				if (!pegou && agora >= c.SegundosAteAVirada) { noPico = l; pegou = true; }
			}, boneco: vis);

			// ============================ "COMPLETAMENTE BRANCO, SO A SILHUETA" ============================
			// 1,0 e o numero, e ele e diferente do banho da transformacao (0,92) DE PROPOSITO -- ver
			// `CharacterVisual.Banhar`, que documenta os dois lados. Com 0,92 o penteado ainda se le, e
			// "so a silhueta" seria mentira.
			// ==========================================================================================
			Conferir(pegou && noPico.Mistura > 0.98f,
					 $"D3 na virada o corpo esta CHAPADO de branco (mistura {noPico.Mistura:0.###})");
			Conferir(pegou && noPico.Cor.R > 0.99f && noPico.Cor.G > 0.99f && noPico.Cor.B > 0.99f,
					 $"D4 ...e a cor e BRANCA e nao a de uma forma ({noPico.Cor.ToHtml(false)})");

			// O BRANCO NAO E UM SOCO: `achatar` zerado e o que separa os dois gestos que dividem o
			// canal. Com o achatamento do golpe, a fusao nasceria espremida por tres segundos.
			Conferir(pegou && noPico.Achatar < 0.001f,
					 $"D5 o branco NAO espreme o corpo (achatar {noPico.Achatar:0.###}) -- ele nao e um soco");

			// --- o escoamento: monotono e chegando a zero no fim da cena ---
			var depois = trilha.Where(x => x.T >= c.SegundosAteAVirada).ToList();
			bool caiSempre = true;
			for (int i = 1; i < depois.Count; i++)
				if (depois[i].M > depois[i - 1].M + 1e-4) caiSempre = false;
			Conferir(depois.Count > 5 && caiSempre,
					 $"D6 o branco ESCOA sem voltar a subir ({depois.Count} amostras)");

			Conferir(depois.Count > 0 && depois[^1].M < 0.1f,
					 $"D7 no fim da cena o branco ja sumiu ({(depois.Count > 0 ? depois[^1].M : -1):0.###}) "
				   + "-- *a cena acaba com a fusao feita*");

			// --- D8: o prazo do branco E a cauda da cena, e nao um numero escrito ---
			// A conta: `Banhar` decai LINEAR, entao a metade da mistura cai na metade do prazo. Se
			// alguem cravar um numero aqui (1,2 s, o do banho da transformacao), esta linha reprova.
			double meio = c.SegundosAteAVirada + (c.Segundos - c.SegundosAteAVirada) / 2;
			var naMetade = depois.Where(x => x.T >= meio).ToList();
			Conferir(naMetade.Count > 0 && naMetade[0].M > 0.3f && naMetade[0].M < 0.7f,
					 $"D8 na METADE da cauda o branco esta na metade ({(naMetade.Count > 0 ? naMetade[0].M : -1):0.##}) "
				   + "-- o prazo e a cauda da cena, e nao um numero escrito");
		}
		finally
		{
			if (IsInstanceValid(t)) t.QueueFree();
			dono.QueueFree();
			irmao.QueueFree();
		}

		OBrancoSobreviveATrocaDeCamadas(cat);
	}

	/// <summary>
	/// ============================ O DEFEITO QUE SO ESTA CENA ENCONTROU ============================
	/// O `flash_cor` era escrito **uma vez**, no `Banhar`, e o `AplicarImpacto` reescrevia so a
	/// mistura. Isso valia enquanto a pilha de camadas do boneco nao mudasse no meio do gesto -- e a
	/// cena da fusao quebra exatamente essa premissa: a lavagem branca comeca no beat que ASSUME, e o
	/// `Virar()` do MESMO instante veste a aparencia da fusao, o que **remonta as camadas**
	/// (`World.VestirCorpoInteiro` -> `CharacterVisual.Vestir`).
	///
	/// As camadas novas nasciam com o `flash` sendo reescrito pelo laco e com o `flash_cor` no PADRAO
	/// DO SHADER (1,00 / 0,95 / 0,90 -- um branco morno). O sintoma seria da pior especie: o corpo
	/// lavando **quase** da cor certa, num gesto de tres segundos, sem nada errado aparente.
	///
	/// E ele nao e da fusao: levar um soco no quadro em que a roupa muda tinha o mesmo defeito, com
	/// 0,15 s pra ninguem ver. Por isso a prova mede o caso GERAL -- banhar, revestir, e conferir que
	/// a cor continua a que foi pedida.
	/// ========================================================================================
	/// </summary>
	private void OBrancoSobreviveATrocaDeCamadas(VisualCatalog cat)
	{
		// ============================ O VEICULO E A ROUPA, E NAO O CORPO ============================
		// A camada do CORPO sobrevive a qualquer `Vestir` (o `Garantir()` a cria uma vez e o `Trocar`
		// so lhe muda a folha), entao ela **nunca** poderia acusar este defeito -- foi a primeira
		// versao desta prova, e ela passou verde com o bug de pe.
		//
		// Quem nasce no meio do gesto e a ROUPA: `_roupa.Add(NovaCamada(2 + i))`, e e exatamente isso
		// que a fusao faz na virada (a `Metamoran Vest` ou o `potara` entram na pilha). Entao o corpo
		// comeca SEM roupa e ganha uma peca durante o escoamento, que e o caso real.
		// ========================================================================================
		Node2D corpo = MontarCorpo(cat, "DonoTroca", Vector2.Zero);
		CharacterVisual vis = Vis(corpo);
		try
		{
			if (vis.LavagemDeTeste == null) { Conferir(false, "D9 sem shader -- nao da pra medir"); return; }

			string? peca = cat.Roupas.FirstOrDefault();
			Conferir(peca != null, $"D9 o catalogo tem peca de roupa pra vestir no meio ({peca ?? "NENHUMA"})");
			if (peca == null) return;

			int camadasAntes = vis.LavagemDaPilhaDeTeste.Camadas;
			vis.Banhar(Colors.White, 3.0, lavagem: 1f);

			// A ROUPA ENTRA NO MEIO DO ESCOAMENTO -- e o que a virada da fusao faz.
			vis.Vestir(cat, new Appearance
			{
				Cabelo = "Vegito",
				Roupa = [new PecaDeRoupa(peca)],
			}, "Saiyan", "Male");

			(int Camadas, int Cores, float Menor) nova = vis.LavagemDaPilhaDeTeste;
			Conferir(nova.Camadas > camadasAntes,
					 $"D10 a peca nova ENTROU na pilha durante o banho ({camadasAntes} -> {nova.Camadas} "
				   + "camadas) -- senao esta prova mede o nada");

			// O QUADRO SEGUINTE. E ele que reescreve as duas cores em TODA camada -- ver
			// `CharacterVisual.AplicarImpacto`.
			vis._Process(0.05);
			(int Camadas, int Cores, float Menor) depois = vis.LavagemDaPilhaDeTeste;

			Conferir(depois.Menor > 0.9f,
					 $"D11 a lavagem alcanca TODA camada, inclusive a que nasceu depois "
				   + $"(menor mistura {depois.Menor:0.###} em {depois.Camadas})");
			Conferir(depois.Cores == 1,
					 $"D12 ...e o boneco inteiro lava da MESMA cor ({depois.Cores} cores distintas na pilha)");
			Conferir(vis.LavagemDeTeste!.Value.Cor.R > 0.99f && vis.LavagemDeTeste!.Value.Cor.G > 0.99f,
					 $"D13 ...e essa cor e o BRANCO pedido, e nao o padrao morno do shader "
				   + $"({vis.LavagemDeTeste!.Value.Cor.ToHtml(false)})");
		}
		finally { SumirAgora(corpo); }
	}

	// =====================================================================
	// E. AS INJECOES -- cada regra recebe o proprio defeito
	// =====================================================================
	/// <summary>
	/// A BANCADA SE COBRA. Cada linha aqui e um defeito PLAUSIVEL (o que alguem escreveria sem ler os
	/// comentarios), e a regra correspondente TEM que ficar vermelha. Regra que passa verde com o
	/// proprio defeito e falha DA BANCADA, e nao do jogo -- foi assim que se descobriu, no passe
	/// anterior, que o `matiz` precisava ser medido junto da cor.
	///
	/// **NENHUMA delas toca a cena de producao.** `Beat` e `Cinematica` sao montados novos a partir
	/// dos beats da original (o `Beat` e `record struct`, o `with` faz copia): envenenar
	/// `Cinematicas.Fusao` deixaria o bloco A da proxima rodada medindo a cena adulterada.
	/// </summary>
	private void AInjecao(VisualCatalog cat)
	{
		GD.Print("[cenafusao] -- E. as injecoes (TEM que ficar vermelhas) --");
		Cinematica c = Cinematicas.Fusao;

		// --- E1: o branco no COMECO da cena (o defeito que o dono ja cobrou duas vezes) ---
		Cinematica adiantada = Reescrever(c, b =>
			Math.Abs(b.Em) < 1e-9 ? b with { Faz = b.Faz | Efeito.SilhuetaBranca }
								  : b with { Faz = b.Faz & ~Efeito.SilhuetaBranca });
		Beat viradaAdiantada = BeatDaVirada(adiantada);
		Conferir(!viradaAdiantada.Faz.HasFlag(Efeito.SilhuetaBranca),
				 "E1 [injecao] com o branco no INSTANTE ZERO, a regra A6 fica vermelha");

		// --- E2: a cena sem cauda (o branco nao teria por onde escoar) ---
		Cinematica semCauda = new()
		{
			Forma = "",
			SegundosPreso = 4.0,
			Beats = [.. c.Beats.Where(b => b.Em <= c.SegundosAteAVirada)],
		};
		bool caudaDaInjecao = semCauda.Beats.Any(b => b.Em > semCauda.SegundosAteAVirada);
		Conferir(!caudaDaInjecao
				 && !(semCauda.Segundos - semCauda.SegundosAteAVirada > Cinematicas.SegundosDoBanho),
				 $"E2 [injecao] sem cauda, as regras A3 e A3b ficam vermelhas "
			   + $"(vira em {semCauda.SegundosAteAVirada:0.#}, acaba em {semCauda.Segundos:0.#})");

		// --- E3: a lavagem do BANHO (0,92) no lugar da cheia -- "so a silhueta" deixa de ser verdade ---
		Node2D corpo = MontarCorpo(cat, "DonoE", Vector2.Zero);
		CharacterVisual vis = Vis(corpo);
		try
		{
			if (vis.LavagemDeTeste != null)
			{
				vis.Banhar(Colors.White, 3.0);   // sem o `lavagem: 1f` -- o padrao da transformacao
				Conferir(vis.LavagemDeTeste!.Value.Mistura <= 0.98f,
						 $"E3 [injecao] com a lavagem PADRAO (0,92) a regra D3 fica vermelha "
					   + $"({vis.LavagemDeTeste!.Value.Mistura:0.###})");

				// ============================ E4: A CAMADA NOVA NASCE ERRADA ============================
				// Esta e a injecao do defeito que esta tarefa encontrou, e ela e do sentido inverso das
				// outras: em vez de escrever o defeito, ela EXPOE o instante em que ele existe. Entre o
				// `Vestir` (a camada nova entra) e o `_Process` seguinte (que reescreve as duas cores em
				// toda camada) a pilha esta com DUAS cores -- e e nesse estado que o jogo ficaria PRA
				// SEMPRE se o `AplicarImpacto` nao reescrevesse o `flash_cor`.
				//
				// A prova exige as duas coisas: que o estado errado EXISTA (senao nao ha o que consertar
				// e a D12 nao mede nada) e que ele SUMA no quadro seguinte.
				// ====================================================================================
				string? p = cat.Roupas.FirstOrDefault();
				if (p != null)
				{
					vis.Banhar(new Color(1, 0, 0), 3.0, lavagem: 1f);
					vis.Vestir(cat, new Appearance
					{
						Cabelo = "Goku",
						Roupa = [new PecaDeRoupa(p)],
					}, "Saiyan", "Male");

					int coresAntes = vis.LavagemDaPilhaDeTeste.CoresDistintas;
					vis._Process(0.05);
					int coresDepois = vis.LavagemDaPilhaDeTeste.CoresDistintas;

					Conferir(coresAntes > 1 && coresDepois == 1,
							 $"E4 [injecao] a camada nova NASCE com outra cor ({coresAntes} cores na pilha) "
						   + $"e e a reescrita por quadro que junta as duas ({coresDepois}) -- "
						   + "sem ela, a D12 fica vermelha pra sempre");
				}
			}
		}
		finally { corpo.QueueFree(); }

		// --- E5: a luz NO CLIMAX (o disco opaco taparia os dois no instante que a cena existe pra mostrar) ---
		Cinematica luzNoClimax = Reescrever(c, b =>
			b.Faz.HasFlag(Efeito.Assumir) ? b with { Faz = b.Faz | Efeito.LuzDaFusao } : b);
		bool climaxTapado = false;
		foreach (Beat b in luzNoClimax.Beats)
			if (b.Em >= luzNoClimax.SegundosAteAVirada && b.Faz.HasFlag(Efeito.LuzDaFusao))
				climaxTapado = true;
		Conferir(climaxTapado,
				 "E5 [injecao] com um estouro NA virada, a regra A5b fica vermelha -- e e a regra que "
			   + "impede a folha opaca de tapar a fusao no instante em que ela acontece");

		// --- E5b: a luz voltando a piscar (os tres `flick` do DU, que o dono cortou pra um) ---
		// A INJECAO INVERTEU DE SENTIDO neste passe, e o registro fica: ela injetava UM beat de luz
		// quando o roteiro tinha tres. Hoje o roteiro tem um -- *"essa animacao do fusion light so toca
		// uma vez"* -- entao o defeito plausivel e o oposto: alguem "consertar" a cena pondo os tres
		// `flick` do `_vfx.dm:8-16` de volta, sem ler por que eles sairam.
		Cinematica luzPiscando = Reescrever(c, b =>
			b.Faz.HasFlag(Efeito.Assumir) ? b : b with { Faz = b.Faz | Efeito.LuzDaFusao });
		Conferir(Quantos(luzPiscando, Efeito.LuzDaFusao) != 1,
				 $"E5b [injecao] com a luz em VARIOS beats, a regra A5 fica vermelha "
			   + $"({Quantos(luzPiscando, Efeito.LuzDaFusao)} estouros contra o 1 que o dono pediu)");

		// --- E5c: a virada solta da folha (o prazo escrito a mao que este passe aposentou) ---
		// O defeito plausivel: alguem acha os 0,7 s "muito rapido" e escreve 4,0 no beat da virada, sem
		// mexer na luz. A cena continua rodando, o branco continua escoando -- e a fusao passa a existir
		// 3,3 s DEPOIS de o clarao ter apagado, com dois corpos parados no meio. E o *"quando ela acabar
		// a fusao ja vai estar pronta"* quebrado sem nada na tela dizendo por que.
		Cinematica viradaSolta = Reescrever(c, b =>
			b.Faz.HasFlag(Efeito.Assumir) ? b with { Em = b.Em + 3.3 } : b);
		Conferir(Math.Abs(viradaSolta.SegundosAteAVirada - Cinematicas.SegundosDaLuzDaFusao) >= 1e-9,
				 $"E5c [injecao] com a virada solta da folha, a regra A2 fica vermelha "
			   + $"(vira em {viradaSolta.SegundosAteAVirada:0.##}s contra "
			   + $"{Cinematicas.SegundosDaLuzDaFusao:0.##}s de animacao)");

		// --- E6: uma forma no campo `Forma` (a chama da cena sairia vermelho sangue, e a pedra sumiria) ---
		var comForma = new Cinematica { Forma = "oozaru", SegundosPreso = 7.0, Beats = c.Beats };
		Conferir(!comForma.OChaoSeSolta,
				 "E6 [injecao] com a cena presa a uma forma que NAO se sobe, a regra A9 fica vermelha "
			   + "(e e por isso que `Forma` e a string vazia)");
	}

	/// <summary>Uma copia da cena com os beats reescritos. NAO toca a original -- ver o bloco E.</summary>
	private static Cinematica Reescrever(Cinematica c, Func<Beat, Beat> f) => new()
	{
		Forma = c.Forma,
		Musica = c.Musica,
		Silhueta = c.Silhueta,
		SegundosPreso = c.SegundosPreso,
		Beats = [.. c.Beats.Select(f)],
	};
}
