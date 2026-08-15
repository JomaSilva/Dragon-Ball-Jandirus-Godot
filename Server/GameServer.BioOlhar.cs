using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O PALCO DOS TRES PEDIDOS VISUAIS DO DONO (`--bioolhar`) ============================
/// Tres dos quatro pedidos desta rodada sao de PIXEL, e nenhum deles se responde com um `bool`:
///
///   1. *"os bio androides o OLHO FICA VOANDO na forma de BARATA, faca com q os olhos SUMAM"* -- ele
///      mandou a FOTO das duas pupilas flutuando acima do casco da larva;
///   4. *"vc n colocou a CINEMATICA DE TRANSFORMACAO dos bio androides... tinha um OVERLAY q fazia o
///      CORPO BRILHAR"*;
///   3. *"ao ter os requisitos... e MORRER, ele vai CANCELAR A MORTE e voltar a vida"*.
///
/// ============================ POR QUE ELE NAO E O `--biovivo` ============================
/// O palco da escada (`GameServer.BioPalco.cs`) fotografa **um degrau por vez, um corpo so, parado**.
/// Nenhuma das tres perguntas acima cabe la:
///
///   * os olhos da larva sao ~4 px num recorte de 76x96 -- eles ficam **muito abaixo** do piso de 3%
///     com que aquela bancada separa um degrau do outro. O defeito que o dono viu na tela e invisivel
///     pra toda a maquinaria de medida dela, e foi por isso que a auditoria anterior o mediu no NODE
///     (`OlhosVisiveisDeTeste`) -- que e "uniform escrito", nao "pixel desenhado";
///   * a cinematica dura 28 s e aquele palco existe justamente pra **nao** ter cena rodando (ele marca
///     `EstreiaVista` no nascimento pra pular todas);
///   * e a morte precisa de DOIS corpos no mesmo quadro -- a prova de que o requisito e o requisito
///     esta em quem NAO volta.
///
/// ============================ TRES CORPOS, E A DIFERENCA ENTRE DOIS DELES E UM CAMPO ============================
///   A -- bio-androide COM DNA Saiyajin na fornada. O protagonista dos tres pedidos.
///   C -- bio-androide SEM DNA, e IDENTICO ao A em tudo o mais: mesmo BP, mesmo degrau, mesma
///        maestria de SSJ1, morto no mesmo instante pela mesma porta. **Um campo os separa**
///        (`bio_saiyan_dna`), e e ele que o dono chama de "os requisitos";
///   B -- um SAIYAJIN, que existe so pra ser o contra-exemplo da cinematica: ele se transforma no
///        MESMO instante que o A, e a pergunta e se as duas cenas saem diferentes na tela.
///
/// ============================ ELE NAO ENCURTA CAMINHO ============================
/// Os corpos nascem pelo `NascerNpc` de producao, viram bio pelo `NascerBioAndroide` de producao (com
/// laboratorio e fornada de verdade), sobem degrau pelo `SubirDegrauDoBio` de producao -- que e quem
/// dispara a cena -- e morrem pelo `Combate.Morrer()` de producao, **sem** `ignorarSeguro`, ou seja
/// passando pelo `NegarMorte` que e a porta unica. O unico atalho e o RELOGIO.
/// ==============================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>`--bioolhar`. Ver o cabecalho.</summary>
	private bool _bioOlhar;

	/// <summary>Os tres corpos em cena. Zero = o palco ainda nao foi montado.</summary>
	private int _olharA, _olharB, _olharC;

	/// <summary>Em que passo ele esta. -1 = montado e ainda sem anunciar nada.</summary>
	private int _olharPasso;

	/// <summary>Quando dar o proximo passo (ms). Zero = acabou.</summary>
	private long _olharProximo;

	/// <summary>A conta de quem esta OLHANDO -- os corpos se plantam em volta dela.</summary>
	private string _olharConta = "";

	/// <summary>
	/// O ELENCO, ANUNCIADO POR ID E NAO POR NOME. O `--biovivo` procura o corpo por nome e paga um
	/// preco por isso: `NascerBioAndroide` RENOMEIA a criatura ("Bio-Androide de X"), entao aquele
	/// robo tem um bloco inteiro explicando que o nome so vale ate achar o corpo.
	///
	/// Aqui sao tres corpos e dois deles trocam de nome no mesmo tique. Mandar os tres ids de uma vez,
	/// no mesmo canal de chat que o jogo ja tem, corta a duvida na raiz.
	/// </summary>
	public const string MarcaDoElencoDoOlhar = "[OLHAR-DO-BIO] elenco ";

	/// <summary>O prefixo do anuncio de passo. Ver <see cref="TickDoOlharDoBio"/>.</summary>
	public const string MarcaDoOlharDoBio = "[OLHAR-DO-BIO] passo ";

	/// <summary>
	/// QUANTO DURA CADA PASSO, um por um -- e eles NAO sao iguais de proposito.
	///
	/// Os dois degraus do meio disparam a cinematica de 28,0 s (`Cinematicas.BioEvoluindo`), e o
	/// fotografo precisa do MEIO dela e ainda do corpo assentado depois. Uma janela unica teria que
	/// ser de 34 s pra todos, e a bancada levaria cinco minutos pra dizer o que diz em dois.
	/// </summary>
	private static readonly double[] SegundosDoPassoDoOlhar = [8, 8, 8, 36, 36, 8, 12, 10];

	/// <summary>
	/// O BP DO DOADOR DE MENTIRA -- o mesmo do `--biovivo`, e pelo mesmo motivo: o bio nasce com
	/// METADE do doador mais forte, e a porta do SSJ2 e cara.
	/// </summary>
	private const double BpDoDoadorDoOlhar = 4e9;

	/// <summary>
	/// ============================ ONDE OS TRES SE PLANTAM, EM TILES DO HOST ============================
	/// CURTO E EM DOIS EIXOS, e o numero foi calculado e nao chutado. A camera segue o host e o zoom
	/// padrao deste jogo e **3x** (`Settings.Zoom = 3`): a seis tiles de distancia um corpo ja esta a
	/// 576 px do centro da tela. Enfileirar os tres no eixo X poria o terceiro fora do quadro -- que e
	/// um modo de falha que o `--biovivo` ja registrou por escrito ("o palco esta FORA DA TELA", quatro
	/// poses perdidas numa rodada inteira).
	///
	/// E OS DOIS BIOS FICAM NA MESMA COLUNA, um acima do outro: sao eles que o dono vai comparar na
	/// foto da morte, e dois corpos lado a lado na vertical cabem na mesma tira sem o cenario do meio.
	/// Quatro tiles de afastamento = 384 px a zoom 3, contra 80 px de altura de recorte: nao se tocam.
	/// ================================================================================================
	/// </summary>
	private static readonly (int X, int Y) TileA = (3, -2), TileC = (3, +2), TileB = (6, 0);

	/// <summary>
	/// MONTA O PALCO: tres corpos em volta de quem entrou, todos ainda gente. Ver <see cref="TileA"/>
	/// sobre as distancias.
	/// </summary>
	private void MontarOOlharDoBio(ServerPlayer pl)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) { GD.PrintErr("[bioolhar] a zona do host nao tem mapa"); return; }

		_olharConta = pl.Conta;
		_olharA = PlantarCorpo(pl, mapa, TileA.X, TileA.Y, "Olhar A");
		_olharC = PlantarCorpo(pl, mapa, TileC.X, TileC.Y, "Olhar C");
		_olharB = PlantarCorpo(pl, mapa, TileB.X, TileB.Y, "Olhar B");

		if (_olharA == 0 || _olharB == 0 || _olharC == 0)
		{ GD.PrintErr("[bioolhar] um dos tres corpos nao nasceu -- o palco nao sobe"); return; }

		// O B E SAIYAJIN, e ele so existe pra ser o CONTRA-EXEMPLO da cinematica: sem um segundo
		// filme rodando no mesmo instante, "a cena do bio tem uma silhueta de luz" nao diz que ela e
		// DELE -- diria so que o tocador acende uma silhueta em qualquer transformacao.
		if (_players.TryGetValue(_olharB, out ServerPlayer? b))
		{
			b.Race = "Saiyan";
			b.Ficha.Race = "Saiyan";
			b.Ficha.ParentRace = "Saiyan";
			b.Ficha.canSSJ = true;
			b.Ficha.BP = BpDoDoadorDoOlhar;
			b.Ficha.Statify();
			b.Forma.Liberar("ssj1");
			b.Forma.Maestria.Por("ssj1", 100);
			b.SigAtributos = "";
			TrocarAparencias(b);
		}

		_olharPasso = -1;
		_olharProximo = NowMs() + 6_000;

		AnunciarNoMundo($"{MarcaDoElencoDoOlhar}{_olharA},{_olharB},{_olharC}");
		GD.Print($"[bioolhar] elenco: A={_olharA} (bio COM DNA), B={_olharB} (Saiyajin), "
			   + $"C={_olharC} (bio SEM DNA). O roteiro comeca em 6 s.");
	}

	/// <summary>Um corpo parado a `tx,ty` tiles do host. `Cerebro = null` pelo motivo do `--biovivo`.</summary>
	private int PlantarCorpo(ServerPlayer pl, ZoneCollision mapa, int tx, int ty, string nome)
	{
		Vec2 onde = mapa.PontoLivrePerto(
			pl.Pos + new Vec2(tx * ZoneCollision.TileSize, ty * ZoneCollision.TileSize));
		ServerPlayer? s = NascerNpc("cidadao", pl.Zone, onde, ++_lugarDaBancadaDeBio);
		if (s == null) return 0;

		s.Cerebro = null;
		s.Name = nome;
		s.Conta = "olhar-do-bio";
		s.Race = "Human";
		s.Ficha.Race = "Human";
		s.Ficha.ParentRace = "Human";
		s.Ficha.Statify();
		s.SigAtributos = "";
		TrocarAparencias(s);
		return s.Id;
	}

	/// <summary>
	/// UM PASSO POR VOLTA. Roda no tique de 1 Hz, ao lado do <see cref="TickDoPalcoDoBio"/>.
	///
	/// ============================ OS OITO PASSOS ============================
	///   0  HUMANO       -- o CONTROLE dos olhos. Sem ele, "a larva nao tem olhos" ficaria verde num
	///                      port que tivesse perdido a camada de olhos inteira, que e o defeito oposto
	///                      e igualmente feio;
	///   1  LARVA        -- **o pedido 1**;
	///   2  IMPERFEITO   -- a carapaca rompe. O olho VOLTA, e essa metade impede o conserto de virar
	///                      exagero;
	///   3  A CENA       -- A e C entram na metamorfose de 28,0 s; B entra no Super Saiyajin no MESMO
	///                      instante. **O pedido 4**, com contra-exemplo de outra raca rodando junto;
	///   4  PERFEITA     -- o degrau que a morte exige;
	///   5  ANTES        -- os requisitos postos no A, e o C fica sem UM deles;
	///   6  A MORTE      -- os dois morrem pela porta unica, no mesmo tique. **O pedido 3**;
	///   7  DEPOIS       -- dez segundos depois: um de pe, o outro nao.
	/// =======================================================================
	/// </summary>
	private void TickDoOlharDoBio()
	{
		if (_olharProximo == 0 || NowMs() < _olharProximo) return;
		if (!_players.TryGetValue(_olharA, out ServerPlayer? a)
			|| !_players.TryGetValue(_olharB, out ServerPlayer? b)
			|| !_players.TryGetValue(_olharC, out ServerPlayer? c))
		{ _olharProximo = 0; GD.PrintErr("[bioolhar] um corpo do palco sumiu"); return; }

		int passo = ++_olharPasso;
		if (passo >= SegundosDoPassoDoOlhar.Length)
		{
			_olharProximo = 0;
			GD.Print("[bioolhar] o roteiro terminou.");
			return;
		}
		_olharProximo = NowMs() + (long)(SegundosDoPassoDoOlhar[passo] * 1000);

		// ============================ O PALCO SEGUE O ELENCO, MAS SO NA VIRADA DO PASSO ============================
		// O `--biovivo` reposiciona o corpo a CADA tique, porque um chefe de saga arrastava o
		// fotografo pelo mapa. Aqui isso nao serve: as medidas desta bancada sao pares de quadros
		// CONSECUTIVOS (o mesmo instante com e sem a camada), e um corpo que se teleporta entre os
		// dois quadros faria o fundo inteiro trocar -- a diferenca sairia enorme e a bancada leria
		// isso como "a camada esta desenhando". Reposicionar so na virada deixa a janela da medida
		// limpa e ainda conserta a camera fugindo.
		// ======================================================================================================
		ServerPlayer? olheiro = _players.Values.FirstOrDefault(
			p => p.Conta == _olharConta && p.Peer != null && p.Zone.Equals(a.Zone));
		if (olheiro != null)
		{
			int t = ZoneCollision.TileSize;
			a.Pos = olheiro.Pos + new Vec2(TileA.X * t, TileA.Y * t);
			c.Pos = olheiro.Pos + new Vec2(TileC.X * t, TileC.Y * t);
			b.Pos = olheiro.Pos + new Vec2(TileB.X * t, TileB.Y * t);
		}
		else GD.PrintErr("[bioolhar] o olheiro nao esta na zona -- as fotos vao sair vazias");

		switch (passo)
		{
			case 0: break;   // o CONTROLE: os tres ainda sao gente

			case 1:
				NascerNoOlhar(a, comDna: true);
				NascerNoOlhar(c, comDna: false);
				GD.Print($"[bioolhar] passo 1: LARVA -- A dna={a.Ficha.bio_saiyan_dna} "
					   + $"canSSJ={a.Ficha.canSSJ} | C dna={c.Ficha.bio_saiyan_dna} "
					   + $"canSSJ={c.Ficha.canSSJ}");
				break;

			case 2:
				SubirDegrauDoBio(a, BioAndroids.Imperfeito);
				SubirDegrauDoBio(c, BioAndroids.Imperfeito);
				break;

			// ============================ A CENA, E O CONTRA-EXEMPLO NO MESMO QUADRO ============================
			// O `SubirDegrauDoBio` dispara a `CenaDoBio` sozinho (e e a porta de producao). O B entra no
			// Super Saiyajin no mesmo tique -- sem `EstreiaVista`, pra a cena INTEIRA rodar.
			// ================================================================================================
			case 3:
				SubirDegrauDoBio(a, BioAndroids.SemiPerfeito);
				SubirDegrauDoBio(c, BioAndroids.SemiPerfeito);
				b.Ficha.Ki = b.Ficha.MaxKi;
				TransformarPara(b, "ssj1");
				GD.Print($"[bioolhar] passo 3: A e C na metamorfose (28,0 s); B em '{b.Forma.Atual}'");
				break;

			case 4:
				SubirDegrauDoBio(a, BioAndroids.Perfeito);
				SubirDegrauDoBio(c, BioAndroids.Perfeito);
				break;

			// ============================ OS REQUISITOS, E O QUE FALTA NO C E **UM CAMPO** ============================
			// `DespertarSsj2DoBio` pede cinco coisas: nascido em laboratorio, DNA Saiyajin, `canSSJ`,
			// forma perfeita, SSJ1 dominado e BP na porta pessoal. Os dois corpos recebem TODAS menos a
			// segunda, que so o A tem -- e ela veio da FORNADA, no passo 1, e nao de uma linha aqui.
			//
			// O BP SAI DA PORTA PESSOAL e nao de um numero escrito: o limiar do SSJ2 e sorteado no
			// nascimento, e cravar um valor aqui seria a segunda verdade sobre onde ele comeca.
			// ====================================================================================================
			case 5:
				PrepararPraMorte(a);
				PrepararPraMorte(c);
				GD.Print($"[bioolhar] passo 5: ANTES -- A: dna={a.Ficha.bio_saiyan_dna} "
					   + $"degrau={a.Ficha.bio_stage} maestria={a.Forma.Maestria.De("ssj1"):0} "
					   + $"BP={a.Ficha.BP:N0} forma='{a.Forma.Atual}' | C: dna={c.Ficha.bio_saiyan_dna} "
					   + $"degrau={c.Ficha.bio_stage} maestria={c.Forma.Maestria.De("ssj1"):0} "
					   + $"BP={c.Ficha.BP:N0} forma='{c.Forma.Atual}'");
				break;

			// ============================ A MORTE, PELA PORTA UNICA E SEM `ignorarSeguro` ============================
			// `Morrer()` devolve **false** quando a morte foi NEGADA. Os dois retornos sao o placar do
			// pedido 3 medido no servidor -- e a foto e o outro lado dele.
			// ====================================================================================================
			case 6:
			{
				// ============================ ELES CAEM PRO LESTE, E ISSO E PRA A FOTO SE LER ============================
				// O sprite de nocaute deste jogo e um desenho JA DEITADO com a cabeca pro LESTE, e o
				// cliente o GIRA pela direcao da queda (`CharacterVisual.DeitarPor`). Caindo pro SUL ele
				// gira -90 graus -- e um desenho deitado girado noventa graus fica **de pe na tela**.
				//
				// Isso nao e defeito: a tabela e literal do dono (*"quando o personagem ta olhando pra
				// baixo e ele desmaia ele ta desmaiando de cabeca pra baixo"*) e quem cai, cai de costas.
				// Mas e a unica das quatro direcoes em que a foto do cadaver e ilegivel -- a primeira
				// rodada desta bancada saiu com o corpo morto identico ao vivo no recorte, e so o
				// `QuadroDoCorpoDeTeste` (o sprite cru, sem rotacao) mostrou que ele estava deitado.
				//
				// LESTE e o angulo zero, entao a queda aparece como o artista a desenhou. Escolher a
				// direcao da queda e a mesma classe de cuidado que o `Cerebro = null` do palco -- nao muda
				// o que se mede (o `ko` e o `dead` sao os mesmos nas quatro direcoes), muda o que se VE.
				// ====================================================================================================
				a.Facing = Facing.East; a.FacingDaQueda = Facing.East;
				c.Facing = Facing.East; c.FacingDaQueda = Facing.East;

				bool morreuA = a.Combate?.Morrer() ?? false;
				bool morreuC = c.Combate?.Morrer() ?? false;
				GD.Print($"[bioolhar] passo 6: A MORTE -- A morreu={morreuA} dead={a.Ficha.dead} "
					   + $"forma='{a.Forma.Atual}' | C morreu={morreuC} dead={c.Ficha.dead} "
					   + $"forma='{c.Forma.Atual}'");
				break;
			}

			case 7:
				GD.Print($"[bioolhar] passo 7: DEPOIS -- A dead={a.Ficha.dead} forma='{a.Forma.Atual}' "
					   + $"ssjBuff={a.Ficha.ssjBuff:0.##} | C dead={c.Ficha.dead} forma='{c.Forma.Atual}'");
				break;
		}

		// ============================ O ELENCO SAI DE NOVO A CADA PASSO, E ISSO CONSERTOU UMA RODADA INTEIRA ============================
		// Anunciado UMA vez, no `MontarOOlharDoBio`, ele nunca chegava: o palco e montado DENTRO do
		// login do host, e o robo da foto so assina o canal de chat no `_Ready` dele -- que roda
		// depois. A rodada saiu com os oito passos anunciados, o roteiro inteiro executado no servidor,
		// e o fotografo com `A=0`: doze linhas de "SEM FOTO: o corpo 0 nao tem posicao desenhada".
		//
		// Repetir e mais barato que sincronizar: sao oito mensagens numa bancada, e o robo ignora as
		// repetidas. A alternativa -- adiar a montagem pra depois de um aperto de mao -- criaria um
		// segundo relogio pra o palco.
		// ==========================================================================================================================
		AnunciarNoMundo($"{MarcaDoElencoDoOlhar}{_olharA},{_olharB},{_olharC}");

		// O ANUNCIO VEM DEPOIS DA ACAO -- ver o mesmo bloco no `--biovivo`: anunciando antes, o
		// fotografo retrata o estado velho com o rotulo novo, e uma rodada inteira sai deslocada em um.
		AnunciarNoMundo($"{MarcaDoOlharDoBio}{passo}");
	}

	/// <summary>
	/// O NASCIMENTO, PELA PORTA DE PRODUCAO -- um laboratorio de verdade e uma fornada de verdade.
	///
	/// O `comDna` e a UNICA diferenca entre os dois corpos deste palco, e ele entra pelo campo que o
	/// jogo usa (`Gestacao.TemSaiyajin`, lido do DNA das amostras em `DNALabs.dm:477-480`) -- nao por
	/// um `bio_saiyan_dna = false` escrito depois, que testaria o campo e nao a fornada.
	/// </summary>
	private void NascerNoOlhar(ServerPlayer s, bool comDna)
	{
		var lab = new Obra
		{
			Id = 984_910 + s.Id % 50, Tipo = "Android_Creation_Mainframe", Aparafusada = true, Lab = 2,
			X = s.Pos.X, Y = s.Pos.Y, DonoConta = s.Conta, DonoNome = s.Name,
		};
		lab.PorZona(s.Zone);
		_noChao.Add(lab);

		var g = new Gestacao
		{
			DonoConta = s.Conta,
			MaiorBp = BpDoDoadorDoOlhar,
			TemSaiyajin = comDna,
			Amostras =
			{
				new Amostra
				{
					Raca = comDna ? "Sayian" : "Human",
					Doador = comDna ? "doador saiyajin" : "doador humano",
					Bp = BpDoDoadorDoOlhar,
				},
			},
		};

		NascerBioAndroide(s, lab, g);

		// A ESTREIA DA **FORMA** JA CONTA COMO VISTA -- e so a de forma. As cenas do bio
		// (`CenaDoBio`) nao passam por `EstreiaVista`: elas nao sao cenas de forma, nao tem versao
		// encurtada e cada degrau acontece uma vez na vida. Ou seja, esta linha nao encolhe nada do
		// que esta sendo fotografado no passo 3 -- ela so impede que a cena de 25 s do Super Saiyajin
		// caia por cima da recomposicao do passo 6.
		s.Forma.EstreiaVista.Add(Jandirus.Core.Forms.Catalogo.Rede("ssj1"));
		s.Forma.EstreiaVista.Add(Jandirus.Core.Forms.Catalogo.Rede("ssj2"));
	}

	/// <summary>
	/// TODOS OS REQUISITOS DO <see cref="DespertarSsj2DoBio"/> **menos o DNA**, que veio da fornada.
	/// Os dois corpos passam por aqui identicos: e isso que faz do C um contra-exemplo e nao um corpo
	/// mais fraco que morreu por outro motivo.
	/// </summary>
	private void PrepararPraMorte(ServerPlayer s)
	{
		s.Forma.Maestria.Por("ssj1", 100);

		Jandirus.Core.Forms.FormaDef? ssj2 = Jandirus.Core.Forms.Catalogo.Def("ssj2");
		if (ssj2 != null)
		{
			double porta = s.Forma.Limiares?.Porta(ssj2) ?? ssj2.PortaBp;
			if (s.Ficha.BP < porta) s.Ficha.BP = porta * 1.05;
		}

		s.Ficha.Statify();
		AplicarForma(s);
		s.SigAtributos = "";
		RepercutirPoder(s);
	}
}
