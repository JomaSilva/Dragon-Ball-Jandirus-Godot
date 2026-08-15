using Godot;
using Jandirus.Core.Combat;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DO MEMBRO PERDIDO (`--diagmembroperdido`). Ela nao confere campo nenhum: ela TIRA FOTO.
///
/// ============================ POR QUE ELA EXISTE, SEPARADA DA `--diagdecalque` ============================
/// A `--diagdecalque` ja prova que a folha `Body Parts Bloody` carrega, que as dez pecas acham o
/// recorte delas e que a tabela membro->arte nao caiu no padrao. Isso e a MAQUINA. O que ela nao
/// alcanca -- e nao tem como alcancar, porque ela chama `Plantar` na mao -- e a pergunta do dono:
///
///     *"ao ter um MEMBRO PERDIDO em combate, sair a animacao do Blood Spray e logo em seguida
///      SPAWNAR NO CHAO o icon do membro perdido"*
///
/// "Em combate" e a parte que so uma briga de verdade responde. Aqui ninguem chama efeito nenhum:
/// dois corpos brigam com golpe LETAL ate um membro zerar, e quem desenha e o `World.AoGolpe` a
/// partir do `HitEvent` que o servidor mandou (o jato) e o `S2C.Decalque` que veio logo atras (a
/// peca). A bancada so aperta o obturador.
///
/// E ELA FOTOGRAFA DUAS VEZES POR AMPUTACAO, em quadros separados, porque o pedido do dono e uma
/// SEQUENCIA e nao um instante -- "e LOGO EM SEGUIDA". Uma foto so no auge do jato mostraria a
/// primeira metade da frase e calaria a segunda:
///
///   1-jato   ~0,12 s depois do golpe: o `Blood Spray` rodando em cima do corpo
///   2-peca   ~0,90 s depois: o jato ja acabou (ele dura 0,5 s) e o que SOBRA e a peca no chao
///
/// A 2 e a que responde o item 3 do roteiro: no mesmo quadro tem que dar pra ver o corpo SEM o
/// membro (os 4 bits de `MascaraDeFeridas.Amputados` ja chegaram e o sprite ja apagou o membro) e a
/// peca caida do lado. Se as duas coisas nao contarem a mesma historia, e nesta foto que aparece.
/// ========================================================================================================
///
/// ============================ AS DUAS FASES: BRACO E DEPOIS PERNA ============================
/// A tabela `Body.PecaDe` tem duas armadilhas anotadas (`Perna` desenha "limb", `Rabo` desenha
/// "guts"), e a `--diagdecalque` audita as duas por NOME. Mas nome de animacao certo com RECORTE
/// trocado dentro da folha fica verde nas duas bancadas e sai errado na tela -- e o modo de falha
/// que este projeto ja deixou passar quatro vezes.
///
/// So a foto separa. Por isso sao DUAS VITIMAS e nao uma: a fase A mira `bracos` no `AlvoBraco`, a
/// fase B mira `pernas` no `AlvoPerna`, e as duas fotos ficam lado a lado. Se sairem IGUAIS, a
/// tabela esta errada -- e nenhum numero teria dito isso.
///
/// A MIRA E DE VERDADE (`SendAim`, o mesmo canal do jogador clicando na regiao do boneco). Ela
/// PESA, nao decide: fora da zona mirada o membro ainda pode ser pego com um quarto do peso
/// (`Body.Sortear`). Entao a bancada nao promete qual membro cai -- ela ANOTA o que caiu, e o nome
/// do arquivo sai do membro REAL. Uma bancada que jurasse "este e o braco" e fotografasse a perna
/// seria pior que nenhuma.
/// ============================================================================================
///
/// ============================ COMO RODAR ============================
/// DOIS processos, e o de cima TEM JANELA (no headless o `GetImage` volta vazio e nao ha foto):
///
///   carrasco (janela, mais forte, e quem fotografa):
///     Godot --path . --host --rede 7974 --aguadentro --vooteste --esquivateste 6 --kiteste \
///           --bpteste 100000 --horateste 0.5 --socar --socaralvo AlvoBraco --socarperto 34 \
///           --diagmembroperdido --raca Human --conta bancada_membro_a --nome Carrasco
///   vitima (headless, mais fraca, vem andando):
///     Godot --headless --path . --rede 7974 --connect 127.0.0.1 --socar --socaralvo Carrasco \
///           --socarperto 34 --raca Human --conta bancada_membro_b --nome AlvoBraco
///
/// Pra a fase das PERNAS, acrescente `--membrofase 1` no carrasco e chame a vitima de `AlvoPerna`.
///
/// AS TRES FLAGS DE AMBIENTE NAO SAO ENFEITE, e cada uma paga uma rodada perdida:
///   * `--aguadentro` poe os DOIS no mesmo lago. Sem berco comum eles nasceram a 5,7 mil pixels um
///     do outro e a rodada inteira virou caminhada.
///   * `--vooteste` porque o berco da Terra tem lago e a agua BARRA quem esta a pe: com os dois no
///     chao o `--socar` empurrou a tecla contra a margem por quatro minutos, 764 socos e ZERO
///     acertos. Ver o bloco do voo em `_Process`.
///   * `--socarperto 34` porque o alcance do soco e 40 px (`CombatMath.Alcance`): parando em 56 o
///     robo fica FORA do proprio alcance e o soco sai no ar.
///
/// `admin_trazer` NAO SERVE aqui, e nao e defeito: o admin por endereco se desliga sozinho quando
/// duas contas chegam do mesmo endereco local (`ConferirAmbiguidadeDeHost`), e uma bancada de dois
/// processos e exatamente esse caso. A regra esta certa; o atalho e que nao existe.
/// ====================================================================
/// </summary>
public partial class RoboDeMembroPerdido : Node
{
	private static GameClient? C => GameClient.Instance;

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];

	private bool _acabou;
	private double _vida;

	/// <summary>Depois disto a bancada desiste e relata o que conseguiu. Briga nao tem hora marcada.</summary>
	private const double Paciencia = 260;

	// =====================================================================
	// AS DUAS FASES
	// =====================================================================
	/// <summary>Nome da vitima, byte da zona mirada (`Protocol.Zonas`) e o rotulo do arquivo.</summary>
	private readonly record struct Fase(string Vitima, byte Mira, string Rotulo);

	private static readonly Fase[] Roteiro =
	[
		new("AlvoBraco", 4, "A-mirando-bracos"),   // Protocol.Zonas[4] = "bracos"
		new("AlvoPerna", 5, "B-mirando-pernas"),   // Protocol.Zonas[5] = "pernas"
	];

	/// <summary>
	/// EM QUAL FASE COMECAR (`--membrofase N`). Zero = a A (bracos), que e o padrao.
	///
	/// ============================ POR QUE ISTO PRECISOU EXISTIR ============================
	/// As duas fases moram no mesmo processo porque a comparacao braco-x-perna e o veredito. Mas
	/// elas dependem de DUAS vitimas vivas e PERTO, e o berco nao promete isso: numa rodada o
	/// `AlvoPerna` nasceu a 5,7 mil pixels do carrasco, do outro lado do mapa. Andar ate la
	/// consome a rodada inteira e a fase B nunca acontece.
	///
	/// Com esta flag a fase B roda sozinha, com UMA vitima nascida junto -- e continua sendo
	/// "corpos diferentes", que e o que o roteiro pede: a perna sai de um corpo que nunca foi o
	/// do braco. O que se perde e a comparacao automatica no fim; ela volta a ser feita olhando
	/// as duas lupas lado a lado, que e como o dono a faria.
	/// ====================================================================================
	/// </summary>
	public int FaseInicial;

	private int _fase;

	/// <summary>Id da vitima da fase corrente, resolvido pelo nome quando ela aparece no snapshot.</summary>
	private int _idDaVez;

	// =====================================================================
	// A CAPTURA EM CURSO
	// =====================================================================
	private bool _capturando;
	private double _relogio;
	private bool _tirou1;
	private string _membroDaVez = "";

	/// <summary>Quando fotografar cada uma, em segundos depois do golpe. Ver o cabecalho.</summary>
	private const double DoJato = 0.22;
	private const double DaPeca = 0.90;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Ready()
	{
		if (C is not { } cli) return;
		cli.Golpe += AoGolpe;
		cli.DecalqueCaiu += AoCairDecalque;
		_fase = Math.Clamp(FaseInicial, 0, Roteiro.Length - 1);
		GD.Print($"[membro] no ar -- fase {_fase}: mirar {Protocol.ZonaDe(Roteiro[_fase].Mira)} "
				 + $"em {Roteiro[_fase].Vitima}");
	}

	public override void _ExitTree()
	{
		if (C is not { } cli) return;
		cli.Golpe -= AoGolpe;
		cli.DecalqueCaiu -= AoCairDecalque;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;

		_vida += delta;

		// ---------- A CAPTURA MANDA EM TUDO ----------
		// Enquanto ela corre nao se troca de alvo nem de mira: o jato dura 0,5 s e a peca acabou de
		// nascer, e mexer no alvo no meio disso moveria a camera pra fora do que se esta fotografando.
		if (_capturando)
		{
			_relogio += delta;
			if (!_tirou1 && _relogio >= DoJato)
			{
				_tirou1 = true;
				Fotografar($"user://membro-{Rotulo()}-1-jato.png", "O JATO (Blood Spray)");
			}
			if (_relogio >= DaPeca)
			{
				Fotografar($"user://membro-{Rotulo()}-2-peca.png", "A PECA NO CHAO");
				// ---------- A LUPA, UMA POR PECA ----------
				// Ver `Lupa`. As duas fotos acima enquadram a CENA; sem esta ninguem consegue dizer
				// se o recorte que caiu e um braco ou um abdomen -- e essa e a pergunta do roteiro.
				for (int k = 0; k < _pecasDaVez.Count; k++)
					Lupa($"user://membro-{Rotulo()}-3-lupa-{k}-{_pecasDaVez[k].Peca}.png",
						 _pecasDaVez[k].Onde, $"LUPA NA PECA {_pecasDaVez[k].Peca}");
				FecharCaptura(cli);
			}
			return;
		}

		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s)"); Fechar(); return; }

		// ---------- APONTA A BRIGA PRA VITIMA DA FASE ----------
		// Quem soca e o `--socar` (o `RoboDeSoco`), que ja anda, ja persegue e ja liga o golpe letal.
		// Esta bancada so diz EM QUEM e ONDE -- ela nao reimplementa perseguicao.
		Fase f = Roteiro[_fase];
		_idDaVez = mundo.IdPeloNome(f.Vitima);
		if (Socador is { } robo && robo.AlvoPreferido != f.Vitima)
		{
			robo.AlvoPreferido = f.Vitima;
			GD.Print($"[membro] fase {_fase}: alvo '{f.Vitima}', mira '{Protocol.ZonaDe(f.Mira)}'");
		}
		cli.SendAim(f.Mira);       // ignora repeticao sozinho (ver `GameClient.SendAim`)

		// ---------- O CARRASCO VOA ----------
		// ============================ POR QUE ELE PRECISA SAIR DO CHAO ============================
		// Quatro rodadas fecharam com centenas de socos e ZERO acertos, e o diario abaixo mostrou o
		// motivo: os DOIS corpos parados, 156 px um do outro, a distancia sem mudar um pixel em
		// quatro minutos. Nao era perseguicao errada -- o berco da Terra tem lago, e o caminho reto
		// ate o outro corpo passa por dentro dele. A agua BARRA quem esta a pe (e e o certo), entao
		// o `--socar` empurrava a tecla contra uma parede pela rodada inteira.
		//
		// Voando o chao deixa de existir pro movimento (o `MoveRules` roda com mapa nulo), e a
		// regra de alcance por altura e ASSIMETRICA a favor de quem paira: quem esta no andar 1
		// alcanca quem esta no chao (`Voo.PodeAcertar`). Ou seja, o carrasco no ar bate em quem
		// esta em terra sem precisar que a vitima faca nada.
		//
		// PELO CANAL DA TECLA (`SendHabilidade("voar")`) e nao por atalho de teste: o servidor
		// cobra o Ki de decolagem e recusa quem nao pode, como recusaria uma pessoa. EXIGE
		// `--vooteste` no servidor -- sem a skill o pedido e negado e a bancada volta ao chao.
		// ======================================================================================
		_redecolar -= delta;
		if (mundo.AlturaDeTeste <= 0f && _redecolar <= 0)
		{
			_redecolar = 3;
			cli.SendHabilidade("voar");
		}

		// ---------- O DIARIO DA APROXIMACAO ----------
		// Duas rodadas fecharam com centenas de socos e ZERO acertos, e o relatorio do `--socar` nao
		// distingue "nao alcancei" de "nao existe alvo": as duas somam em `erros`. Estas tres linhas
		// custam nada e respondem sozinhas -- se a distancia nao cai, e perseguicao; se ela cai e o
		// soco continua no ar, e o CONE (o alcance do soco e 40 px, `CombatMath.Alcance`).
		_diario -= delta;
		if (_diario <= 0)
		{
			_diario = 5;
			Vector2? eu = mundo.PosicaoLocal;
			Vector2? ela = _idDaVez != 0 ? mundo.PosicaoDesenhadaDe(_idDaVez) : null;
			GD.Print($"[membro] eu {eu} (altura {mundo.AlturaDeTeste:0}) | '{f.Vitima}' id {_idDaVez} em {ela}"
					 + (eu != null && ela != null ? $" | {(ela.Value - eu.Value).Length():0} px" : " | SEM ALVO NA TELA")
					 + $" | zona {cli.Zone} | Ki {cli.Sheet.Ki:0}");
		}

		// ---------- SE A BRIGA NAO ACONTECE, PUXA A VITIMA ----------
		// ============================ POR QUE ISTO PRECISOU EXISTIR ============================
		// Duas rodadas desta bancada fecharam com 764 socos e ZERO acertos. O `--socar` acha o alvo
		// (o log diz "alvo PEDIDO ... = id 151") e anda na direcao dele apertando tecla, como um
		// jogador -- e e justamente por ser como um jogador que ele empaca: o berco tem lago, e o
		// caminho reto ate o outro corpo passa por agua, por parede e por NPC. Perseguicao a pe nao
		// e o que esta em teste aqui; o que esta em teste e o que aparece na tela QUANDO o membro
		// cai. Uma bancada que passa quatro minutos andando nao mede nada.
		//
		// `admin_trazer` (o host e admin por IP) poe a vitima EM CIMA de mim. E o unico atalho, e
		// ele nao encosta em nada do que se mede: a amputacao continua saindo do `MeleeResolver`,
		// com golpe letal, mira de verdade e o membro zerando a soco.
		// =====================================================================================
		_puxar -= delta;
		if (_puxar <= 0 && _idDaVez != 0)
		{
			_puxar = 6;
			cli.SendVerbo("admin_trazer", f.Vitima);
		}

		// O LETAL, DE TEMPOS EM TEMPOS E NAO POR QUADRO. `SendLethal` nao filtra repeticao (ao
		// contrario do `SendAim`): mandado todo quadro ele vira um pacote confiavel e uma linha de
		// log do servidor por quadro. Quem liga na entrada e o proprio `--socar`; aqui e so o
		// cinto de seguranca, pro caso de alguma regra desligar no meio.
		_reafirmaLetal -= delta;
		if (_reafirmaLetal <= 0) { _reafirmaLetal = 5; cli.SendLethal(true); }
	}

	private double _reafirmaLetal;

	/// <summary>Segundos ate o proximo `admin_trazer`. Ver o bloco em <see cref="_Process"/>.</summary>
	private double _puxar;

	/// <summary>Segundos ate a proxima linha do diario de aproximacao.</summary>
	private double _diario;

	/// <summary>Segundos ate tentar decolar de novo. Ver o bloco do voo em <see cref="_Process"/>.</summary>
	private double _redecolar;

	/// <summary>O `--socar` do mesmo processo. E ele que anda e bate; aqui so se escolhe o alvo.</summary>
	private RoboDeSoco? Socador => GetTree().Root.FindChild("RoboDeSoco", true, false) as RoboDeSoco;

	// =====================================================================
	// O GATILHO
	// =====================================================================
	/// <summary>
	/// UMA AMPUTACAO DE VERDADE, vinda do servidor.
	///
	/// O `Decepou` chega no pacote CHEIO porque eu sou o atacante (ver `AnunciarGolpe`: os dois
	/// envolvidos recebem o relato confiavel, a plateia recebe o magro sem `Membro`). E por isso que
	/// esta bancada consegue nomear o arquivo pelo membro real -- de fora da briga ela nao
	/// conseguiria, e o proprio sigilo do BP e quem manda nisso.
	/// </summary>
	private void AoGolpe(Protocol.HitEvent h)
	{
		if (_acabou || _capturando || C is not { } cli) return;
		if (h.Atacante != cli.LocalId || !h.Decepou) return;
		if (_idDaVez != 0 && h.Alvo != _idDaVez) return;

		_capturando = true;
		_relogio = 0;
		_tirou1 = false;
		_membroDaVez = h.Membro ?? "";
		_pecasDaVez.Clear();
		_idDaVez = h.Alvo;
		GD.Print($"[membro] AMPUTOU: '{_membroDaVez}' em {h.Alvo} -- obturador armado");
	}

	/// <summary>
	/// AS PECAS QUE O SERVIDOR MANDOU PLANTAR NESTA AMPUTACAO -- no PLURAL, e com a coordenada.
	///
	/// ============================ UM MEMBRO NAO CAI SOZINHO ============================
	/// `Body.Decepar` devolve uma LISTA e nao um membro: arrancar o braco leva a mao junto (ela e
	/// `Aninhado` nele), e o `MeleeResolver.Anotar` carimba as DUAS em `PecasCaidas`. A primeira
	/// versao disto guardava so a ultima que chegou, e a rodada fechou dizendo que um BRACO
	/// arrancado tinha virado a peca `Mao` -- o que e meia verdade e le como tabela errada.
	///
	/// A COORDENADA vem junto porque e ela que mira a lupa. Sem isso a unica ampliacao disponivel
	/// era em volta do CORPO, e o corpo anda (arremesso, nado): a rodada anterior enquadrou a peca
	/// numa foto e a perdeu na seguinte, e nao havia como saber se ela tinha sumido ou saido do
	/// quadro. A peca fica onde caiu -- e o unico ponto fixo desta cena.
	/// ==================================================================================
	/// </summary>
	private readonly List<(PecaDeCorpo Peca, Vector2 Onde)> _pecasDaVez = [];

	private void AoCairDecalque(Protocol.Decal tipo, Jandirus.Core.World.Vec2 onde, Jandirus.Core.World.Facing dir, PecaDeCorpo peca)
	{
		if (_capturando && tipo == Protocol.Decal.Membro)
			_pecasDaVez.Add((peca, new Vector2((float)onde.X, (float)onde.Y)));
	}

	private string Rotulo()
	{
		string membro = _membroDaVez.Length > 0 ? _membroDaVez : "membro";
		return $"{Roteiro[_fase].Rotulo}-{membro.Replace(' ', '-')}";
	}

	private void FecharCaptura(GameClient cli)
	{
		// ---------- O QUE A FOTO TEM QUE MOSTRAR, EM TEXTO AO LADO ----------
		// Nao pra substituir a foto: pra que quem a olha depois saiba o que procurar nela. Uma foto
		// sem legenda de "era pra ter caido um BRACO" nao se conferere sozinha meses depois.
		string amp = cli.Feridas.TryGetValue(_idDaVez, out MascaraDeFeridas m)
			? m.Amputados.ToString() : "(sem mascara)";

		string lista = string.Join(", ", _pecasDaVez.Select(p => $"{p.Peca}@{p.Onde.X:0},{p.Onde.Y:0}"));
		Conferir(_pecasDaVez.Count > 0,
			$"fase {Roteiro[_fase].Rotulo}: caiu '{_membroDaVez}' -> {_pecasDaVez.Count} peca(s): {lista}");
		Conferir(amp != "Nenhum" && amp != "(sem mascara)",
			$"e o CORPO perdeu o membro no sprite ao mesmo tempo: bits amputados = {amp}");

		// ONDE ESTAVA CADA UM. E o que desfaz a duvida de quem le a foto depois: qual dos dois
		// bonecos e a vitima. A camera segue o corpo LOCAL, entao a posicao de tela nao basta.
		Nota($"corpo local em {World.Instancia?.PosicaoLocal}, vitima ({_idDaVez}) em "
			 + $"{World.Instancia?.PosicaoDesenhadaDe(_idDaVez)}");

		// A PECA MAIS "GRAUDA" da cascata e a que rotula a fase: arrancar o braco derruba braco E
		// mao, e "a fase do braco deu Mao" nao responde a pergunta do dono.
		_pecas[_fase] = _pecasDaVez.Count > 0 ? _pecasDaVez[0].Peca : PecaDeCorpo.Nenhuma;
		_membros[_fase] = _membroDaVez;

		_capturando = false;
		_fase++;
		if (_fase >= Roteiro.Length)
		{
			// ---------- AS DUAS FASES DERAM A MESMA PECA? ----------
			// A pergunta do roteiro do dono, e a unica que precisa das DUAS. Se braco e perna
			// sairem no mesmo recorte, a tabela esta errada -- e nenhuma das duas fotos sozinha
			// teria dito isso.
			Conferir(_pecas[0] != _pecas[1] || _pecas[0] == PecaDeCorpo.Nenhuma,
				$"braco e perna NAO desenham a mesma peca ({_pecas[0]} x {_pecas[1]})");
			Fechar();
			return;
		}
		GD.Print($"[membro] fase {_fase}: trocando pra {Roteiro[_fase].Vitima}");
	}

	private readonly PecaDeCorpo[] _pecas = new PecaDeCorpo[2];
	private readonly string[] _membros = ["", ""];

	// =====================================================================
	// A FOTO
	// =====================================================================
	/// <summary>
	/// A tela inteira MAIS um recorte 3x em volta da VITIMA -- e nao em volta de mim.
	///
	/// O jato nasce colado no corpo de quem perdeu o membro e a peca cai num raio de um tile dele
	/// (`rand(-32,32)` do servidor). No zoom do jogo isso e um borrao de 32 px numa tela de 1600:
	/// a tela cheia prova o ENQUADRAMENTO (os dois corpos, a briga acontecendo), o recorte prova o
	/// PIXEL (o recorte da peca, o coto no sprite).
	/// </summary>
	private void Fotografar(string destino, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add($"  ok     {rotulo}: {caminho}");

			if (Recorte(img, NaTelaDaVitima()) is { } lupa)
				lupa.SavePng(caminho.Replace(".png", "-zoom.png"));
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	private Vector2 NaTelaDaVitima()
	{
		if (World.Instancia?.PosicaoDesenhadaDe(_idDaVez) is not { } p) return Vector2.Zero;
		return (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * p;
	}

	/// <summary>
	/// A LUPA: 64 px de mundo em volta de UM PONTO, ampliados 8x.
	///
	/// A `Fotografar` acima enquadra a cena; esta responde a pergunta que a cena nao responde. No
	/// zoom do jogo a peca tem 32 px numa tela de 1920, e "isto e um braco ou um abdomen?" nao se
	/// decide nesse tamanho -- a folha `Body Parts Bloody` tem dez recortes e varios deles sao
	/// manchas vermelhas de vinte pixels. Foi exatamente onde a rodada anterior empacou.
	///
	/// O PONTO E DO MUNDO e nao da tela: a camera segue o corpo local, entao entre uma foto e a
	/// seguinte a mesma peca cai em coordenadas de tela diferentes.
	/// </summary>
	private void Lupa(string destino, Vector2 noMundo, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return;
		Vector2 tela = (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * noMundo;
		const int lado = 64;
		var r = new Rect2I((int)tela.X - lado / 2, (int)tela.Y - lado / 2, lado, lado);
		r = r.Intersection(new Rect2I(0, 0, img.GetWidth(), img.GetHeight()));
		if (r.Size.X < 16 || r.Size.Y < 16) { Nota($"{rotulo}: caiu fora da tela"); return; }
		Image corte = img.GetRegion(r);
		corte.Resize(corte.GetWidth() * 8, corte.GetHeight() * 8, Image.Interpolation.Nearest);
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			corte.SavePng(caminho);
			_passos.Add($"  ok     {rotulo}: {caminho}");
		}
		catch (Exception e) { Nota($"{rotulo}: sem lupa: {e.Message}"); }
	}

	private static Image? Recorte(Image cheia, Vector2 tela)
	{
		if (tela == Vector2.Zero) return null;
		const int lado = 224;
		var r = new Rect2I((int)tela.X - lado / 2, (int)tela.Y - lado / 2, lado, lado);
		r = r.Intersection(new Rect2I(0, 0, cheia.GetWidth(), cheia.GetHeight()));
		if (r.Size.X < 32 || r.Size.Y < 32) return null;
		Image corte = cheia.GetRegion(r);
		corte.Resize(corte.GetWidth() * 3, corte.GetHeight() * 3, Image.Interpolation.Nearest);
		return corte;
	}

	private void Fechar()
	{
		_acabou = true;
		GD.Print("\n[membro] ===== BANCADA DO MEMBRO PERDIDO =====");
		foreach (string l in _passos) GD.Print("[membro] " + l);
		GD.Print($"[membro]   fase A ('{_membros[0]}') -> {_pecas[0]}");
		GD.Print($"[membro]   fase B ('{_membros[1]}') -> {_pecas[1]}");
		GD.Print(_falhas.Count == 0
			? "[membro] ===== TUDO OK ====="
			: $"[membro] ===== {_falhas.Count} FALHA(S) =====\n[membro]   " + string.Join("\n[membro]   ", _falhas));
		GetTree().Quit();
	}
}
