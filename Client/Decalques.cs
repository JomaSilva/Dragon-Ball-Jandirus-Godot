using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// DECALQUES DE CHAO: as marcas que o mundo ganha e perde.
///
/// Sulco de arrasto, cratera, fumaca, terra revirada, agua se afastando e respingo de sangue. Seis
/// coisas diferentes num sistema so porque, pro DESENHO, elas sao a mesma: um sprite no chao, atras
/// dos corpos, com prazo. Seis sistemas paralelos dariam seis tetos pra calibrar e seis lugares pra
/// esquecer de apagar.
///
/// ============================ DE ONDE VEM CADA UM ============================
/// SO O RASTRO DE ARREMESSO vem do servidor (`Protocol.S2C.Decalque`), e por um motivo: ele depende
/// da distancia TOTAL do arremesso (>= 8 tiles, regra do DU), e no meio do voo o cliente nao tem
/// como saber se o corpo vai parar no proximo tile ou dez adiante.
///
/// Os outros quatro o cliente decide sozinho, porque sao consequencia de coisas que ele JA VE: uma
/// celula caiu, um corpo esta sobre agua, um corpo esta se acabando. Mandar isso pela rede seria
/// pagar bytes por uma conta que as duas pontas fazem igual -- e o mesmo argumento que ja tinha
/// posto o ceu, o clima e o universo do lado do cliente.
/// ==============================================================================
///
/// ============================ POR QUE ELES NAO SAO ETERNOS ============================
/// No DU o sulco dura 600 tiques e a cratera 50. Aqui e prazo em segundos, e o teto de decalques
/// vivos existe pelo mesmo motivo do teto da poeira: numa briga longa, marca que nao some vira
/// mil nodes no mapa e o quadro cai. O mais VELHO sai quando o teto estoura.
/// =====================================================================================
/// </summary>
public partial class Decalques : Node2D
{
	/// <summary>Quantos decalques com PRAZO podem estar vivos ao mesmo tempo. Ver o cabecalho.</summary>
	public const int MaxVivos = 120;

	/// <summary>Prazo zero = nao expira. Ver `Prazo`.</summary>
	private const double Permanente = 0;

	/// <summary>
	/// TETO DOS PERMANENTES, separado.
	///
	/// O chao danificado nao expira, entao ele nao pode dividir a fila com os que expiram -- o
	/// despejo por mais-velho apagaria justamente a marca que era pra ficar. Mas teto ele tem: numa
	/// sessao longa o estrago acumula, e "nao expira" nao pode virar "cresce pra sempre".
	///
	/// 3000 e generoso de proposito: uma briga que derruba 800 celulas pinta ~2800 marcas. Acima
	/// disso a mais velha sai, e a diferenca nao se ve.
	/// </summary>
	public const int MaxPermanentes = 3000;

	private readonly List<Node2D> _permanentes = [];

	/// <summary>Quantos decalques permanentes estao no mapa. So pras bancadas.</summary>
	public static int PermanentesDeTeste;

	/// <summary>Quanto cada tipo fica na tela, em segundos.</summary>
	private static double Prazo(Protocol.Decal t) => t switch
	{
		// O DU deixa o sulco 600 tiques (~60 s). Aqui e menos: la o mapa era pequeno e a briga
		// acontecia num lugar so; um minuto de sulco em cada troca de socos entulha o chao.
		Protocol.Decal.Sulco or Protocol.Decal.SulcoPonta => 20,
		Protocol.Decal.Cratera => 18,
		// A GRANDE FICA MAIS TEMPO. Ela e o buraco de uma transformacao de raiva -- some antes da
		// luta acabar seria negar o que acabou de acontecer ali.
		Protocol.Decal.CrateraGrande => 40,
		Protocol.Decal.Fumaca => 2.6,
		// ============================ O CHAO DANIFICADO NAO SOME ============================
		// Decisao do dono: a terra revirada FICA. Ela so desaparece quando o jogo reinicia e o
		// planeta pre-feito e recarregado do zero -- que e o mesmo instante em que as paredes
		// derrubadas voltam. Faz sentido e e coerente: a marca conta que ALI houve estrago, e o
		// estrago em si tambem e permanente ate o mapa ser refeito.
		//
		// `Permanente` (0) tira o decalque do relogio E do teto de vivos -- ver `Plantar`.
		// ===================================================================================
		Protocol.Decal.ChaoDanificado => Permanente,
		// `sleep(20)` no DU, com tick_lag de 0,1 s = 2 s. Este e literal.
		Protocol.Decal.Agua => 2,
		Protocol.Decal.Sangue => 25,
		// ============================ O MEMBRO FICA MAIS QUE TODO O RESTO ============================
		// No BYOND a peca dura 600 s (`spawn(6000) src.loc = null`, `mobparts.dm:395-397`). Dez
		// minutos aqui e insustentavel pelo mesmo motivo do cabecalho -- ela dividiria o teto com a
		// poeira, que numa briga pinta dezenas de marcas por segundo.
		//
		// 60 s e o numero que a peca precisa: ela nao e enfeite de impacto, ela CONTA que alguem
		// perdeu um braco ali, e sumir em cinco segundos negaria a coisa mais grave que acontece
		// numa luta. Quem garante que ela chega la e a fila propria -- ver `MaxPecas`.
		// =============================================================================================
		Protocol.Decal.Membro => 60,
		_ => 10,
	};

	/// <summary>
	/// TETO PROPRIO DAS PECAS DE CORPO, dentro do teto geral. Sao 32 bracos, pernas e visceras no
	/// chao ao mesmo tempo; a 33a empurra a mais velha.
	///
	/// ============================ POR QUE ELA NAO DIVIDE A FILA COM A POEIRA ============================
	/// O despejo por mais-velho e certo pra marca de impacto e errado pra esta: cratera e fumaca
	/// nascem AOS MONTES (uma queda pinta poeira, cratera e ate nove manchas de terra), e uma briga
	/// de meio minuto queimaria os 120 lugares varias vezes. A peca, que e rara e dura 60 s, sairia
	/// da tela em segundos -- justamente numa briga longa, que e quando ela tem o que contar.
	///
	/// A guarda e nos DOIS sentidos: a peca nao e despejada pela poeira (o despejo geral sacrifica
	/// primeiro quem nao e peca) e a poeira nao morre afogada em pecas (32 de 120 e o que elas podem
	/// ocupar). Mesma logica que ja separou os PERMANENTES -- fila que se mistura e teto que engana.
	/// ====================================================================================================
	/// </summary>
	public const int MaxPecas = 32;

	/// <summary>
	/// O RECORTE DA FOLHA `Body Parts Bloody` de cada peca. A TABELA em si e do Core
	/// (<see cref="Jandirus.Core.Combat.Body.PecaDe"/>, com as linhas do BYOND) -- aqui so se
	/// traduz simbolo em nome de animacao, que e arte e por isso mora do lado do cliente.
	///
	/// Os nomes sao os `icon_state` do original em minusculas, que e como o conversor de `.dmi`
	/// escreveu as dez animacoes do `.tres`. Repare em `Perna => "limb"`: e a armadilha anotada no
	/// Core, e ela sobrevive ate aqui.
	/// </summary>
	private static string Recorte(PecaDeCorpo p) => p switch
	{
		PecaDeCorpo.Cabeca => "head",
		PecaDeCorpo.Cerebro => "brain",
		PecaDeCorpo.Torso => "torso",
		PecaDeCorpo.Abdomen => "abdomen",
		PecaDeCorpo.Visceras => "guts",
		PecaDeCorpo.Reprodutor => "sorgans",
		PecaDeCorpo.Braco => "arm",
		PecaDeCorpo.Mao => "hands",
		PecaDeCorpo.Perna => "limb",
		PecaDeCorpo.Pe => "foot",
		_ => "",
	};

	/// <summary>A folha de cada tipo, e o prefixo da animacao dentro dela.</summary>
	private static (string Folha, string Anim) Arte(Protocol.Decal t) => t switch
	{
		Protocol.Decal.Sulco => ("res://Assets/Sprites/Decor/craterseries.tres", "crater"),
		Protocol.Decal.SulcoPonta => ("res://Assets/Sprites/Decor/craterseries.tres", "begin"),
		Protocol.Decal.Cratera => ("res://Assets/Sprites/Decor/Craters.tres", "small_crater"),
		// OUTRA FOLHA, e por isso ela pode ser grande de verdade: a `Craters` so tem o recorte
		// `small_crater`, e esticar um sprite pequeno so da borrao. A `big crater` e 96x96.
		Protocol.Decal.CrateraGrande => ("res://Assets/Sprites/DU/Map/big crater.tres", "default"),
		// ============================ ESTA E TEXTURA SOLTA, NAO FOLHA ============================
		// `dust cloud 2018.png` e um PNG de 1280x720 no DU -- arte, e nao sprite sheet com metadados.
		// Ele nem tinha vindo na conversao: minha varredura pegou so `*.dmi`, e o DU tem 133 imagens
		// soltas ao lado dos icones. O `.tres` nao existe pra ele, e nem deveria.
		// ========================================================================================
		Protocol.Decal.Fumaca => ("res://Assets/Sprites/DU/VFX/dust cloud 2018.png", ""),
		Protocol.Decal.ChaoDanificado => ("res://Assets/Sprites/DU/VFX/Damaged Ground.tres", ""),
		Protocol.Decal.Agua => ("res://Assets/Sprites/DU/VFX/KiWater.tres", ""),
		Protocol.Decal.Sangue => ("res://Assets/Sprites/DU/VFX/Blood spray.tres", ""),
		// CUIDADO COM OS HOMONIMOS desta pasta: ha `Body Parts.tres`, `BodyParts.tres` e
		// `Body Parts Bloodless.tres`. A folha certa e a "Bloody" -- e a que o dono nomeou e a unica
		// com sangue nos recortes. O prefixo fica vazio porque a animacao vem por nome do
		// `Recorte()`; nao ha sufixo de direcao aqui.
		Protocol.Decal.Membro => ("res://Assets/Sprites/Misc/Body Parts Bloody.tres", ""),
		_ => ("", ""),
	};

	/// <summary>1280x720 de arte pra um buraco de um tile: a fumaca nasce pequena.</summary>
	private const float EscalaDaFumaca = 0.07f;

	/// <summary>Quantas vezes ela incha ate se dissolver.</summary>
	private const float CrescimentoDaFumaca = 2.6f;

	private readonly Dictionary<string, SpriteFrames?> _folhas = [];

	/// <summary>
	/// Os decalques com prazo. `Peca` marca o membro arrancado, que o despejo protege -- ver
	/// <see cref="MaxPecas"/>. E um campo da FILA e nao do node porque quem precisa saber e o
	/// despejo, e ele so olha esta lista.
	/// </summary>
	private readonly List<(Node2D No, double Morre, bool Peca)> _vivos = [];
	private double _relogio;

	/// <summary>Quantos foram PEDIDOS e quantos estao vivos. So pras bancadas -- ver o teto.</summary>
	public static int PedidosDeTeste, VivosDeTeste;

	/// <summary>
	/// Quantos RESPINGOS DE SANGUE foram pedidos. So pra bancada, e separado do
	/// <see cref="PedidosDeTeste"/> de proposito: o contador geral soma cratera, poeira, sulco e a
	/// agua que o voo abre, e uma bancada que perguntasse "o sangue respingou?" olhando pra ele
	/// ficaria verde por causa de uma poca d'agua.
	///
	/// Existe porque o gatilho do respingo passou a viver do GRAU DE FERIDA (ver
	/// `World.Decalques.cs`) depois que a vida alheia saiu do snapshot -- e um consumidor que mudou
	/// de fonte precisa de uma medida que diga que ele CONTINUA disparando.
	/// </summary>
	public static int SangueDeTeste;

	/// <summary>
	/// ONDE caiu o ultimo respingo. So pra bancada, e o contador acima nao serve sem ela: numa zona
	/// povoada quem sangra pode ser um NPC do outro lado do mapa, e um teste que so contasse pingos
	/// nao saberia dizer de QUEM eles sao -- ficaria vermelho por causa de uma briga alheia. Com a
	/// posicao da pra atribuir o respingo ao corpo que estava ao lado dele.
	/// </summary>
	public static Vector2 OndeSangrouDeTeste;

	/// <summary>
	/// Quantas PECAS DE CORPO foram pedidas e qual foi a ultima. So pra bancada, e separado do
	/// contador geral pelo mesmo motivo do sangue: uma pergunta como "o braco arrancado caiu no
	/// chao?" olhando pro contador geral ficaria verde por causa de uma nuvem de poeira.
	///
	/// A PECA JUNTO E O QUE TORNA O TESTE HONESTO: a armadilha desta tabela nao e a peca nao cair, e
	/// cair a ERRADA (`Perna` desenha "limb", `Rabo` desenha "guts"). Um contador sozinho nao
	/// distingue um pe de um braco e ficaria verde com a tabela inteira trocada.
	/// </summary>
	public static int MembrosDeTeste;

	/// <inheritdoc cref="MembrosDeTeste"/>
	public static PecaDeCorpo UltimaPecaDeTeste;

	public static Decalques? Instancia { get; private set; }

	public override void _Ready()
	{
		Instancia = this;
		// ATRAS DOS CORPOS. Decalque e chao: desenhado por cima, ele taparia o personagem que
		// acabou de cair nele.
		ZIndex = -2;
		YSortEnabled = false;
	}

	public override void _ExitTree() { if (Instancia == this) Instancia = null; }

	/// <summary>Some com tudo. Chamado na troca de zona -- marca da Terra nao vai pra Namek.</summary>
	/// <summary>
	/// Some com tudo -- inclusive os permanentes.
	///
	/// Chamado na troca de zona, que e o mesmo instante em que o cenario derrubado tambem volta ao
	/// que o arquivo diz (ver `ZoneCollision.FecharTudo`). As duas coisas somem juntas porque sao a
	/// mesma coisa: o mapa foi recarregado do zero.
	/// </summary>
	public void Limpar()
	{
		foreach ((Node2D no, _, _) in _vivos) Apagar(no);
		foreach (Node2D no in _permanentes) Apagar(no);
		_vivos.Clear();
		_permanentes.Clear();
		VivosDeTeste = 0;
		PermanentesDeTeste = 0;
	}

	/// <summary>
	/// TIRA DA ARVORE E DEPOIS LIBERA.
	///
	/// `QueueFree` sozinho nao basta: ele so libera no FIM do quadro, e ate la o node continua
	/// filho e continua desenhando. A bancada pegou isso -- `Limpar()` seguido de `GetChildCount()`
	/// devolvia os 120 nodes que tinham acabado de ser "apagados".
	/// </summary>
	private void Apagar(Node2D no)
	{
		if (!IsInstanceValid(no)) return;
		RemoveChild(no);
		no.QueueFree();
	}

	/// <summary>Quantas pecas de corpo estao no chao agora. Ver <see cref="MaxPecas"/>.</summary>
	private int ContarPecas()
	{
		int n = 0;
		foreach ((_, _, bool peca) in _vivos) if (peca) n++;
		return n;
	}

	/// <summary>
	/// Despeja o MAIS VELHO que atende o criterio. Devolve falso quando nao ha nenhum -- que e o
	/// caso em que o despejo geral desiste de poupar as pecas e come a mais velha delas.
	///
	/// A varredura e linear numa lista de no maximo <see cref="MaxVivos"/> e so roda quando o teto
	/// estoura, nao por quadro.
	/// </summary>
	private bool DespejarPrimeira(Func<(Node2D No, double Morre, bool Peca), bool> criterio)
	{
		int i = _vivos.FindIndex(v => criterio(v));
		if (i < 0) return false;
		Apagar(_vivos[i].No);
		_vivos.RemoveAt(i);
		return true;
	}

	/// <summary>
	/// PLANTA UM DECALQUE. <paramref name="dir"/> escolhe a variante direcional das folhas que tem
	/// uma (o sulco); nas outras ele e ignorado.
	/// </summary>
	/// <param name="peca">
	/// SO PRO <see cref="Protocol.Decal.Membro"/>: qual recorte da folha `Body Parts Bloody` cai.
	/// Nos outros tipos e ignorado. Ver <see cref="Recorte"/>.
	/// </param>
	public void Plantar(Protocol.Decal tipo, Vector2 onde, Facing dir,
						PecaDeCorpo peca = PecaDeCorpo.Nenhuma)
	{
		PedidosDeTeste++;
		if (tipo == Protocol.Decal.Sangue) { SangueDeTeste++; OndeSangrouDeTeste = onde; }
		if (tipo == Protocol.Decal.Membro) { MembrosDeTeste++; UltimaPecaDeTeste = peca; }

		(string caminho, string prefixo) = Arte(tipo);
		if (caminho.Length == 0) return;

		bool solta = caminho.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
		SpriteFrames? folha = null;
		string anim = "";

		if (!solta)
		{
			if (!_folhas.TryGetValue(caminho, out folha))
				_folhas[caminho] = folha = GD.Load<SpriteFrames>(caminho);
			if (folha == null) return;

			// A PECA ESCOLHE PELO NOME, nao pelo sorteio do `Escolher`: a folha tem dez recortes que
			// sao dez COISAS diferentes, e sortear entre eles derrubaria um pe quando arrancaram um
			// braco. Peca sem recorte (`Nenhuma`) nao planta nada, e e o certo -- e o unico caso em
			// que o servidor nao disse o que caiu.
			anim = tipo == Protocol.Decal.Membro ? Recorte(peca) : Escolher(folha, prefixo, dir, tipo);
			if (anim.Length == 0) return;
		}

		double prazo = Prazo(tipo);
		bool permanente = prazo <= 0;

		// ============================ O TETO DISPARA, E TEM QUE DISPARAR ============================
		// Sem isto uma briga de dois minutos deixaria centenas de nodes no mapa. O mais VELHO sai --
		// a marca nova e a que conta a briga que esta acontecendo agora.
		//
		// AS DUAS FILAS SAO SEPARADAS: o permanente nao pode ser despejado pelo teto dos que
		// expiram, senao o chao danificado sumiria justamente numa briga longa -- que e quando ele
		// mais tem o que contar.
		// ===========================================================================================
		if (permanente)
		{
			while (_permanentes.Count >= MaxPermanentes)
			{
				Apagar(_permanentes[0]);
				_permanentes.RemoveAt(0);
			}
		}
		else
		{
			bool ehPeca = tipo == Protocol.Decal.Membro;

			// A PECA TAMBEM TEM COTA -- ver `MaxPecas`. Sem esta linha, uma briga de dez pessoas se
			// destrocando encheria os 120 lugares de bracos e a poeira sumiria do jogo.
			if (ehPeca)
				while (ContarPecas() >= MaxPecas) DespejarPrimeira(v => v.Peca);

			// O DESPEJO SACRIFICA PRIMEIRO QUEM NAO E PECA. Nao e favoritismo: e que os dois nascem
			// em ritmos incomparaveis. Cratera, fumaca e terra revirada saem aos montes de cada
			// queda; o membro arrancado e o evento mais raro do combate. Na fila crua o mais VELHO
			// sai, e o mais velho e sempre a peca -- ela duraria segundos em vez dos 60 s.
			while (_vivos.Count >= MaxVivos)
				if (!DespejarPrimeira(v => !v.Peca)) DespejarPrimeira(_ => true);
		}

		// A CRATERA CRESCE, como no DU (`animate(transform = matrix()*craterMaxSize...)`); a fumaca
		// nasce PEQUENA porque a arte dela tem 1280x720 pra cobrir um buraco de um tile.
		var escala = tipo switch
		{
			Protocol.Decal.Cratera => new Vector2(0.05f, 0.05f),
			Protocol.Decal.CrateraGrande => new Vector2(0.05f, 0.05f),
			Protocol.Decal.Fumaca => new Vector2(EscalaDaFumaca, EscalaDaFumaca),
			_ => Vector2.One,
		};

		Node2D s;
		if (solta)
		{
			var tex = GD.Load<Texture2D>(caminho);
			if (tex == null) return;
			s = new Sprite2D { Texture = tex, Position = onde, Scale = escala, Modulate = new Color(1, 1, 1, 0.75f) };

			// ============================ A FUMACA FICA ACIMA DO CORPO ============================
			// O node dos decalques inteiro vive em `ZIndex = -2` -- decalque e CHAO, e desenhado por
			// cima taparia quem esta em pe nele. Fumaca nao: ela sobe, e passar na frente do
			// personagem e exatamente o que ela tem que fazer. Pedido do dono.
			//
			// `ZAsRelative = false` e o que tira este node da camada do pai: sem isso o `ZIndex`
			// seria somado ao -2 e a fumaca continuaria atras de todo mundo.
			// =====================================================================================
			if (tipo == Protocol.Decal.Fumaca) { s.ZIndex = 100; s.ZAsRelative = false; }

			AddChild(s);
		}
		else
		{
			var a = new AnimatedSprite2D { SpriteFrames = folha, Animation = anim, Position = onde, Scale = escala };
			AddChild(a);
			a.Play();
			s = a;
		}

		if (tipo is Protocol.Decal.Cratera or Protocol.Decal.CrateraGrande)
		{
			// O DU sorteia 75%..115% do tamanho cheio -- duas crateras nunca sao iguais.
			//
			// A DE RAIVA ABRE MUITO MAIS. O dono: "as big craters estao pequenas ainda, aumente bem
			// o tamanho". Sao DOIS fatores multiplicados, e por isso ela cresce tanto: a folha ja e
			// 3x maior (96x96 contra o recorte pequeno) e este alvo triplica por cima -- um buraco
			// de uns 8 tiles, que e o que se ve quando alguem vira Super Saiyajin pela primeira vez.
			float alvo = (float)GD.RandRange(0.75, 1.15)
					   // 3,0 cobria uns 15 tiles e engolia a tela (o dono fotografou). A folha ja e
					   // 3x maior que o recorte pequeno, entao este fator MULTIPLICA aquilo -- e o
					   // erro foi tratar os dois como se fossem um so.
					   * (tipo == Protocol.Decal.CrateraGrande ? 1.4f : 1.0f);
			s.CreateTween().TweenProperty(s, "scale", new Vector2(alvo, alvo), 0.35)
				.SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		}

		if (tipo == Protocol.Decal.Fumaca)
		{
			// ============================ A FUMACA CRESCE E SE DISSOLVE ============================
			// Nuvem de poeira nao aparece pronta e some: ela INCHA enquanto perde corpo. Duas
			// propriedades animadas em PARALELO -- a escala sobe a vida inteira, a opacidade cai
			// depois de um respiro. Em sequencia (o padrao do Tween) ela cresceria opaca e so depois
			// comecaria a sumir, que le como sprite trocando de tamanho, nao como fumaca.
			// ======================================================================================
			Tween f = s.CreateTween();
			f.SetParallel(true);
			f.TweenProperty(s, "scale", escala * CrescimentoDaFumaca, prazo)
				.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
			f.TweenProperty(s, "modulate:a", 0f, prazo * 0.8).SetDelay(prazo * 0.2);
		}

		if (permanente)
		{
			_permanentes.Add(s);
			PermanentesDeTeste = _permanentes.Count;
			return;
		}

		// APAGA ESMAECENDO. Sumir de uma vez le como bug de desenho; esmaecer le como marca velha.
		//
		// A FUMACA NAO PASSA POR AQUI: ela ja tem o proprio par de tweens (cresce + dissolve), e um
		// segundo tween sobre o MESMO `modulate:a` faria os dois disputarem a propriedade quadro a
		// quadro -- a nuvem piscaria em vez de sumir.
		if (tipo != Protocol.Decal.Fumaca)
		{
			Tween t = s.CreateTween();
			t.TweenInterval(prazo * 0.7);
			t.TweenProperty(s, "modulate:a", 0f, prazo * 0.3);
		}

		_vivos.Add((s, _relogio + prazo, tipo == Protocol.Decal.Membro));
		VivosDeTeste = _vivos.Count;
	}

	/// <summary>
	/// A ANIMACAO CERTA da folha: com sufixo de direcao quando ela tem, sorteada quando o prefixo
	/// e vazio (o sangue tem 13 respingos e o dono pediu um aleatorio).
	/// </summary>
	private static string Escolher(SpriteFrames folha, string prefixo, Facing dir, Protocol.Decal tipo)
	{
		string[] todas = [.. folha.GetAnimationNames()];
		if (todas.Length == 0) return "";

		// A AGUA TEM DOIS ESTADOS SO, e a escolha e do DU (`Unsorted 2.dm:375-380`): "NS" pra quem
		// passa no eixo norte-sul, "EW" pra leste-oeste. Nao e o sufixo de quatro direcoes das
		// outras folhas -- a onda e a mesma indo e voltando no mesmo eixo.
		if (tipo == Protocol.Decal.Agua)
		{
			string eixo = dir is Facing.North or Facing.South ? "ns" : "ew";
			if (folha.HasAnimation(eixo)) return eixo;
		}

		if (prefixo.Length > 0)
		{
			string comDir = $"{prefixo}_{MoveRules.FacingSuffix(dir)}";
			if (folha.HasAnimation(comDir)) return comDir;
			if (folha.HasAnimation(prefixo)) return prefixo;
		}

		// SORTEIO ENTRE AS DE MESMO SUFIXO, pra nao misturar direcoes: o `Blood spray` tem 13
		// respingos x 4 direcoes, e sortear entre as 52 daria o mesmo respingo virado pra outro lado
		// com um terco da chance.
		string suf = "_" + MoveRules.FacingSuffix(dir);
		string[] doLado = [.. todas.Where(a => a.EndsWith(suf, StringComparison.Ordinal))];
		string[] pool = doLado.Length > 0 ? doLado : todas;
		return pool[GD.RandRange(0, pool.Length - 1)];
	}

	public override void _Process(double delta)
	{
		_relogio += delta;
		if (_vivos.Count == 0) return;

		// A LISTA E VARRIDA DE TRAS PRA FRENTE porque ela e ordenada por NASCIMENTO, e quem morre
		// primeiro nem sempre e o primeiro da lista (os prazos sao diferentes por tipo).
		for (int i = _vivos.Count - 1; i >= 0; i--)
		{
			if (_relogio < _vivos[i].Morre) continue;
			_vivos[i].No.QueueFree();
			_vivos.RemoveAt(i);
		}
		VivosDeTeste = _vivos.Count;
	}
}
