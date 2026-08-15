using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO RASTRO DA AGUA (`--diagrastro`). Quatro sentidos, quatro FOTOS -- e uma quinta, com um
/// RAIO cruzando o mesmo lago (ver `ORaioCruzaOLago`): a onda do ki e a mesma do voo, e o eixo dela
/// sai do rumo do proprio tiro.
///
/// ============================ POR QUE ELA EXISTE, SE A `--diagdecalque` JA MEDE ISSO ============================
/// A `--diagdecalque` (passos 5 a 8) ja le, do node que nasceu, qual RECORTE a onda recebeu -- e foi
/// ela que provou o conserto do `DirecaoDe`. Isso e a ponta mais perto do pixel que da pra ler sem
/// foto, e nao e o pixel.
///
/// O que sobra e exatamente o que o dono mandou: ele nao mandou um numero, ele mandou uma FOTO -- o
/// corpo sobre a agua azul com os riscos desenhados EM PE, apontando pra cima, voasse ele pra onde
/// voasse. Duas coisas ficam de fora de qualquer leitura de campo:
///
///   * o recorte certo pode estar desenhado ERRADO na folha (nome "ew" com riscos verticais dentro).
///     Nome de animacao confere com nome de animacao; ninguem confere com o desenho.
///   * a onda pode nascer LONGE do corpo, ou atras dele, ou fora da agua. A `--diagdecalque` planta
///     em (9999, 9999) de proposito, porque ela mede a maquina -- ali nao ha corpo nenhum por perto.
///
/// Por isso aqui nao se planta nada na mao: o corpo VOA sobre o lago apertando tecla, e quem planta
/// e o `World.DesenharDecalques`, do jeito que planta pro jogador.
/// ==============================================================================================================
///
/// ============================ A ORDEM DOS QUATRO NAO E DECORATIVA ============================
/// LESTE, OESTE, NORTE, SUL -- e nao "os quatro em volta". Os pares se DESFAZEM: 0,4 s pra leste e
/// 0,4 s pra oeste devolvem o corpo ao ponto de partida, e o mesmo com norte/sul. Sem isso a quarta
/// rajada sairia do lago (o berco `--aguanoar` poe o corpo sobre o MEIO do lago, nao sobre um oceano)
/// e a ultima foto seria de um corpo sobre a grama, sem onda nenhuma -- verde por ausencia.
///
/// A foto sai COM A TECLA AINDA APERTADA, e nao depois de soltar: a onda tem cadencia de 2 s por
/// celula (o `sleep(20)` do DU) e o corpo parado nao pinta mais nenhuma. Fotografar depois de parar
/// pegaria a onda que sobrou da rajada, que e a mesma coisa -- ate o dia em que nao for.
/// ============================================================================================
///
/// COMO RODAR -- um processo so, e ele PRECISA de janela (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 7974 --aguanoar --vooteste --kiteste --bpteste 3000000 \
///           --horateste 0.5 --diagrastro --raca Human --conta bancada_rastro --nome Rastro
///
/// `--aguanoar` poe o corpo NO AR sobre o meio do lago, que e o unico berco em que as quatro
/// pernas cabem dentro da agua. `--horateste 0.5` e meio-dia: a hora do mundo e sorteada a cada
/// rodada e uma foto de noite nao responde "de que lado a onda esta desenhada".
///
/// As fotos saem em `user://rastro-*.png`, com um recorte 4x ao lado (`-zoom`).
/// </summary>
public partial class RoboDeRastroDaAgua : Node
{
	private static GameClient? C => GameClient.Instance;

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];

	private bool _acabou;
	private double _t;
	private int _passo;
	private double _vida;

	/// <summary>Quanto tempo cada rajada de tecla dura. Ver o cabecalho -- os pares se desfazem.</summary>
	private const double Rajada = 0.40;

	/// <summary>
	/// A PAUSA ENTRE UMA RAJADA E A SEGUINTE. Cada celula so aceita uma onda a cada 2 s (o
	/// `sleep(20)` do DU, guardado no `_ondaAte`), e o QUADRADO abaixo ja garante celula nova em
	/// tres das quatro pernas -- esta folga cobre a quarta, que volta pela coluna de onde tudo saiu.
	///
	/// A cadencia e a regra do jogo; quem tem que se adaptar e o teste. Foi o furo que a segunda
	/// rodada desta bancada abriu contra si mesma: a volta imediata nao plantava NADA e a bancada
	/// reprovou o jogo por um defeito dela.
	/// </summary>
	private const double EsperaDeCelula = 0.9;

	/// <summary>
	/// Quanto a bancada INSISTE numa perna cujo estado ainda nao e o que ela quer medir.
	///
	/// ============================ A FOTO SO VALE NO ESTADO CERTO ============================
	/// A segunda rodada fotografou duas pernas com o corpo sendo ARREMESSADO (o `Empurrado` do
	/// snapshot -- um NPC do berco acertou o corpo pairando no lago). Arremessado, quem manda no
	/// rumo e o `_dirDoCorpo` e nao a tecla, entao a bancada leu "oeste" com a tecla de NORTE
	/// apertada -- e ISSO ESTA CERTO, e o que o `OlharDeTeste` promete. O errado foi apertar o
	/// obturador num instante que nao era o do teste.
	///
	/// Entao a perna nao fotografa no relogio: ela fotografa quando o corpo esta de fato voando
	/// PRA ONDE ela mandou, SOBRE A AGUA. Se em `Insistencia` segundos isso nao acontecer, ela
	/// registra a falha com o que leu -- que e honesto e continua sendo uma falha.
	/// ======================================================================================
	/// </summary>
	private const double Insistencia = 2.5;

	/// <summary>Depois disto a bancada desiste. O berco pode nao ter achado lago nenhum.</summary>
	private const double Paciencia = 120;

	/// <summary>Um sentido: tecla, rotulo e o `Facing` esperado.</summary>
	private readonly record struct Rumo(string Tecla, string Rotulo, Facing Espero);

	/// <summary>
	/// OS QUATRO EM QUADRADO -- leste, norte, oeste, sul.
	///
	/// ============================ POR QUE NAO E "IDA E VOLTA" ============================
	/// A ordem obvia (leste/oeste, norte/sul) desfaz os pares e devolve o corpo ao ponto de
	/// partida -- e foi a primeira que esta bancada usou. Ela tem um defeito que so aparece
	/// rodando: a VOLTA passa pelas MESMAS celulas que a ida acabou de molhar, e nenhuma onda
	/// nova nasce ali por 2 s. Metade das fotos saia sem o efeito que elas existem pra mostrar.
	///
	/// O quadrado resolve os dois de uma vez: cada perna anda por celulas VIRGENS (tres delas por
	/// construcao, e a quarta pela `EsperaDeCelula`) e o corpo termina onde comecou -- que e o que
	/// impede a quarta perna de sair do lago. O berco `--aguanoar` poe o corpo sobre o MEIO de um
	/// lago, nao sobre um oceano.
	/// ====================================================================================
	/// </summary>
	private static readonly Rumo[] Roteiro =
	[
		new("move_right", "1-leste", Facing.East),
		new("move_up",    "2-norte", Facing.North),
		new("move_left",  "3-oeste", Facing.West),
		new("move_down",  "4-sul",   Facing.South),
	];

	/// <summary>O recorte que a onda recebeu em cada rumo -- pra a comparacao final.</summary>
	private readonly string[] _recortes = ["", "", "", ""];

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (Decalques.Instancia is not { } dec) return;

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s)"); Fechar(); return; }

		// ---------- O CORPO TEM QUE ESTAR NO AR ----------
		// O rastro so nasce com `altura > 0` (ver `World.DesenharDecalques`), e e assim que tem que
		// ser: e o efeito de VOAR POR CIMA da agua. Sem isto a bancada mediria o nada em silencio,
		// que e o modo de falha mais caro que existe aqui.
		if (_passo == 0)
		{
			_t += delta;
			if (_t < 3) return;      // deixa o berco assentar
			if (mundo.AlturaDeTeste <= 0f)
			{
				_t = 2.4;            // tenta de novo daqui a pouco
				cli.SendHabilidade("voar");
				return;
			}
			Conferir(SobreAgua(mundo), "o corpo comeca NO AR e SOBRE A AGUA (berco `--aguanoar`)");
			_passo = 1;
			_t = 0;
			return;
		}

		int i = _passo - 1;

		// ---------- DEPOIS DOS QUATRO SENTIDOS, O RAIO ----------
		// Ver `ORaioCruzaOLago`. Ele vem no fim de proposito: o corpo ja esta pairando sobre o meio do
		// lago, que e a unica coisa que este berco garante -- e e dali que sai a celula de agua.
		if (i == Roteiro.Length) { Comparar(); _passo++; _t = 0; return; }

		// ============================ A ESPERA QUE ESTA BANCADA JA APRENDEU UMA VEZ ============================
		// A trava da onda e POR CELULA e vale pra TODO MUNDO (e o `turf.ki_water` do DU): as celulas
		// que o corpo acabou de sobrevoar nas quatro pernas ficam 2 s sem aceitar onda nova -- de quem
		// quer que seja, inclusive do raio. Sem esta pausa a bancada punha o raio numa celula ainda
		// molhada e reprovava o jogo por um defeito DELA. Foi exatamente o que aconteceu na primeira
		// rodada com o raio (tres falhas, recorte vazio), e e a mesma licao que a `EsperaDeCelula` ja
		// tinha ensinado nas quatro pernas.
		// ======================================================================================================
		if (i == Roteiro.Length + 1)
		{
			_t += delta;
			if (_t < SegundosDeOnda + 0.2) return;
			PlantarORaio(mundo, dec);
			_passo++;
			_t = 0;
			return;
		}

		if (i == Roteiro.Length + 2)
		{
			_t += delta;
			if (_t < 0.35) return;   // dois ou tres quadros: o `TickDosDecalques` roda no `_Process`
			ORaioCruzaOLago(mundo, dec);
			Fechar();
			return;
		}
		if (i > Roteiro.Length + 2) { Fechar(); return; }

		Rumo r = Roteiro[i];

		// ---------- A PAUSA, ANTES DE TUDO ----------
		// Ver `EsperaDeCelula`. O corpo fica parado (nenhuma tecla apertada), a cadencia de cada
		// celula vence, e so entao a rajada comeca a contar.
		_t += delta;
		if (_t < EsperaDeCelula) return;

		// ---------- A RAJADA ----------
		if (_antesDaRajada < 0) _antesDaRajada = dec.GetChildCount();
		Godot.Input.ActionPress(r.Tecla);
		if (_t < EsperaDeCelula + Rajada) return;

		// ---------- INSISTE ATE O ESTADO SER O DO TESTE ----------
		// Ver `Insistencia`. Fotografar no relogio ja pegou o corpo sendo arremessado por um NPC do
		// berco; a foto sai quando o corpo esta voando PRA ONDE esta perna mandou, e sobre a agua.
		Facing leu = mundo.DirecaoDoRastroDeTeste;
		string recorte = RecorteNovoDaOnda(dec, _antesDaRajada);
		bool pronto = leu == r.Espero && recorte.Length > 0 && SobreAgua(mundo) && mundo.AlturaDeTeste > 0f;
		if (!pronto && _t < EsperaDeCelula + Rajada + Insistencia) return;

		// ---------- A FOTO, COM A TECLA AINDA APERTADA ----------
		_recortes[i] = recorte;

		Fotografar($"user://rastro-{r.Rotulo}.png", $"VOANDO PRA {r.Rotulo.ToUpperInvariant()}");

		Conferir(leu == r.Espero,
			$"{r.Rotulo}: o corpo LOCAL le {r.Espero} (leu {leu})");
		Conferir(recorte.Length > 0,
			$"{r.Rotulo}: nasceu onda de verdade sob o corpo (recorte \"{recorte}\")");
		Conferir(SobreAgua(mundo),
			$"{r.Rotulo}: e o corpo continua SOBRE A AGUA na hora da foto");

		Godot.Input.ActionRelease(r.Tecla);
		_passo++;
		_t = 0;
		_antesDaRajada = -1;
	}

	/// <summary>Quantos nodes o chao tinha ANTES da rajada. -1 = a rajada ainda nao comecou.</summary>
	private int _antesDaRajada = -1;

	/// <summary>
	/// O RECORTE QUE A ONDA RECEBEU nesta rajada -- lido do NODE, nao do que se pediu.
	///
	/// Entre a direcao e o desenho ainda ha o `Decalques.Escolher`, e ele ja errou antes. Ler o que
	/// se pediu mediria a INTENCAO; e esse o cego que ja deixou quatro bugs visuais passarem por
	/// bancada verde neste projeto.
	/// </summary>
	private static string RecorteNovoDaOnda(Decalques dec, int desde)
	{
		string achou = "";
		for (int k = desde; k < dec.GetChildCount(); k++)
			if (dec.GetChild(k) is AnimatedSprite2D a)
			{
				string nome = a.Animation.ToString();
				if (nome is "ns" or "ew") achou = nome;
			}
		return achou;
	}

	/// <summary>A celula debaixo do corpo e agua? Mesma pergunta que o desenho faz.</summary>
	private static bool SobreAgua(World mundo)
	{
		if (mundo.PosicaoLocal is not { } p) return false;
		int t = ZoneCollision.TileSize;
		return mundo.EhAgua(new Vector2I((int)MathF.Floor(p.X / t), (int)MathF.Floor(p.Y / t)));
	}

	// =====================================================================
	// O RAIO CRUZANDO O LAGO
	// =====================================================================
	/// <summary>Id fora de qualquer faixa de tiro de verdade.</summary>
	private const int IdDoRaio = 970_300;

	/// <summary>
	/// Quanto tempo uma celula fica sem aceitar onda nova -- o `sleep(20)` do DU. E o MESMO numero que
	/// o `World.SegundosDeOnda` usa; esta copia e da bancada e existe pra ela saber ESPERAR a regra do
	/// jogo, nao pra defini-la.
	/// </summary>
	private const double SegundosDeOnda = 2.0;

	/// <summary>Onde o raio foi posto, e quantos nodes o chao tinha antes dele. Ver `PlantarORaio`.</summary>
	private Vector2 _centroDoRaio;
	private int _antesDoRaio = -1;

	/// <summary>
	/// POE UM RAIO SOBRE O LAGO, LONGE DO CORPO -- e o "longe" e a metade que faz a medicao valer.
	///
	/// O corpo esta pairando sobre a agua e pintando as ondas dele. Se o raio nascesse na mesma celula,
	/// a onda que a bancada fotografasse poderia ser a do CORPO, e o teste ficaria verde com o efeito
	/// do tiro desligado. Duas celulas de distancia bastam (a trava de onda e por celula), e o que se
	/// afirma la embaixo e que a onda nova nasceu EM CIMA da celula do raio.
	///
	/// O raio nasce pelo caminho de producao (`World.TiroDeTeste` chama o mesmo `AoNascerTiro` e o
	/// mesmo `AoMoverTiros` do servidor), com a CAUDA a oeste da cabeca: rumo leste, eixo "ew".
	/// </summary>
	private void PlantarORaio(World mundo, Decalques dec)
	{
		if (mundo.PosicaoLocal is not { } eu) { Nota("o raio nao foi plantado: sem corpo local"); return; }

		int t = ZoneCollision.TileSize;
		var minha = new Vector2I((int)MathF.Floor(eu.X / t), (int)MathF.Floor(eu.Y / t));

		Vector2I? alvo = null;
		for (int raio = 2; raio <= 6 && alvo == null; raio++)
			for (int dx = -raio; dx <= raio && alvo == null; dx++)
				for (int dy = -raio; dy <= raio; dy++)
				{
					if (Math.Abs(dx) < 2 && Math.Abs(dy) < 2) continue;   // longe do corpo
					var c = new Vector2I(minha.X + dx, minha.Y + dy);
					if (!mundo.EhAgua(c)) continue;
					alvo = c;
					break;
				}

		if (alvo is not { } celula) { Nota("o raio nao foi plantado: nao achei agua livre perto"); return; }

		_centroDoRaio = new Vector2((celula.X + 0.5f) * t, (celula.Y + 0.5f) * t);
		_antesDoRaio = dec.GetChildCount();
		mundo.TiroDeTeste(IdDoRaio, _centroDoRaio, _centroDoRaio - new Vector2(96, 0),
						  Jandirus.Core.Combat.TipoDeProjetil.Beam);
	}

	/// <summary>
	/// O RAIO ABRE ONDA -- e ela e a MESMA do voo, no eixo do PROPRIO raio.
	///
	/// ============================ ISTO E PORTE, E NAO EFEITO NOVO ============================
	/// No DM quem molha o lago nao e so o corpo: o `Move()` do tiro chama `t.ki_water(dir)`
	/// (`DU/Projectiles.dm:279`), e o `KiWater()` do Finale monta a imagem com `dir=M.dir` do BLAST
	/// (`Turfs.dm:58`). O port tinha so a metade do corpo. Esta e a outra.
	/// ========================================================================================
	///
	/// TRES AFIRMACOES, e as tres precisam ser separadas: que NASCEU onda, que ela nasceu SOB O RAIO
	/// (e nao sob o corpo, que esta ondulando ao lado) e que o recorte e o do EIXO DELE. A ultima e a
	/// unica que reprova o `_ => South` -- as duas primeiras ficariam verdes com a onda em pe.
	/// </summary>
	private void ORaioCruzaOLago(World mundo, Decalques dec)
	{
		if (_antesDoRaio < 0) { Conferir(false, "o raio de bancada chegou a ser plantado sobre o lago"); return; }

		Facing dir = mundo.DirecaoDoTiroDeTeste(IdDoRaio);
		(string recorte, bool noLugar) = OndaNovaDoRaio(dec, _antesDoRaio);

		Fotografar("user://rastro-5-raio.png", "O RAIO CRUZANDO O LAGO");

		Conferir(dir == Facing.East,
			$"o raio (cauda a oeste da cabeca) le East pelo rumo dele mesmo (leu {dir})");
		Conferir(recorte.Length > 0, $"...e nasceu onda de verdade debaixo dele (recorte \"{recorte}\")");
		Conferir(noLugar, "...na celula DO RAIO, e nao na do corpo que paira ao lado");
		Conferir(recorte == "ew",
			$"...e no eixo DELE: \"ew\" pra quem cruza no leste-oeste (recorte \"{recorte}\")");

		mundo.TirarTiroDeTeste(IdDoRaio);
	}

	/// <summary>
	/// A onda que nasceu DEPOIS do raio ser posto: o recorte, e se ela caiu em cima dele.
	///
	/// Le do NODE (nome da animacao e posicao), e nao do que se pediu -- entre a direcao e o desenho
	/// ainda ha o `Decalques.Escolher`, e e ele que ja errou antes.
	/// </summary>
	private (string Recorte, bool NoLugar) OndaNovaDoRaio(Decalques dec, int desde)
	{
		for (int k = desde; k < dec.GetChildCount(); k++)
		{
			if (dec.GetChild(k) is not AnimatedSprite2D a) continue;
			string nome = a.Animation.ToString();
			if (nome is not ("ns" or "ew")) continue;
			if (a.Position.DistanceTo(_centroDoRaio) > 1f) continue;
			return (nome, true);
		}

		// NENHUMA EM CIMA DO RAIO: devolve a ultima que nasceu (se houver), pra o relato dizer O QUE
		// apareceu em vez de so "nao apareceu".
		string sobrou = "";
		for (int k = desde; k < dec.GetChildCount(); k++)
			if (dec.GetChild(k) is AnimatedSprite2D a && a.Animation.ToString() is "ns" or "ew")
				sobrou = a.Animation.ToString();
		return (sobrou, false);
	}

	// =====================================================================
	// O VEREDITO QUE PRECISA DOS QUATRO
	// =====================================================================
	/// <summary>
	/// A FOTO DO DONO tinha os quatro sentidos com o MESMO desenho. Quatro conferencias separadas
	/// passariam com a folha devolvendo "ns" nas quatro; esta e a que nao passa.
	///
	/// E o esperado sao DOIS recortes e nao quatro: a folha `KiWater` tem `ns` e `ew`, porque a onda
	/// e simetrica indo e voltando. Regra do proprio DU (`Unsorted 2.dm:375-380`), nao simplificacao
	/// do port -- entao a bancada cobra leste == oeste e norte == sul, e os dois pares DIFERENTES.
	/// </summary>
	private void Comparar()
	{
		// Os indices seguem o QUADRADO: 0 leste, 1 norte, 2 oeste, 3 sul.
		Conferir(_recortes[0].Length > 0 && _recortes[0] == _recortes[2],
			$"leste e oeste dao o MESMO eixo (\"{_recortes[0]}\" x \"{_recortes[2]}\") -- a onda e simetrica");
		Conferir(_recortes[1].Length > 0 && _recortes[1] == _recortes[3],
			$"norte e sul dao o MESMO eixo (\"{_recortes[1]}\" x \"{_recortes[3]}\")");
		Conferir(_recortes[0].Length > 0 && _recortes[0] != _recortes[1],
			$"e os DOIS EIXOS sao diferentes (\"{_recortes[0]}\" x \"{_recortes[1]}\") "
			+ "-- e esta a que a foto do dono reprovaria");
	}

	// =====================================================================
	// A FOTO
	// =====================================================================
	private void Fotografar(string destino, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add($"  ok     {rotulo}: {caminho}");
			if (Recorte(img, NaTela()) is { } lupa) lupa.SavePng(caminho.Replace(".png", "-zoom.png"));
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	private Vector2 NaTela()
	{
		if (C is not { } cli || World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is not { } eu)
			return Vector2.Zero;
		return (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * eu;
	}

	private static Image? Recorte(Image cheia, Vector2 tela)
	{
		if (tela == Vector2.Zero) return null;
		const int lado = 192;
		var r = new Rect2I((int)tela.X - lado / 2, (int)tela.Y - lado / 2, lado, lado);
		r = r.Intersection(new Rect2I(0, 0, cheia.GetWidth(), cheia.GetHeight()));
		if (r.Size.X < 32 || r.Size.Y < 32) return null;
		Image corte = cheia.GetRegion(r);
		corte.Resize(corte.GetWidth() * 4, corte.GetHeight() * 4, Image.Interpolation.Nearest);
		return corte;
	}

	private void Fechar()
	{
		_acabou = true;
		foreach (Rumo r in Roteiro) Godot.Input.ActionRelease(r.Tecla);
		GD.Print("\n[rastro] ===== BANCADA DO RASTRO DA AGUA =====");
		foreach (string l in _passos) GD.Print("[rastro] " + l);
		GD.Print(_falhas.Count == 0
			? "[rastro] ===== TUDO OK ====="
			: $"[rastro] ===== {_falhas.Count} FALHA(S) =====\n[rastro]   " + string.Join("\n[rastro]   ", _falhas));
		GetTree().Quit();
	}
}
