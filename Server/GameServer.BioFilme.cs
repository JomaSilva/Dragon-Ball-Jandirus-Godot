using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O PALCO DO **FILME** DA CINEMATICA (`--biofilme`) ============================
/// O dono, com foto: *"o bio androide ta MUDANDO O CORPO ANTES DA CINEMATICA ACABAR ai ta ficando
/// BUGADO como pode ver"* -- na imagem dele o corpo aparece **meio trocado**, dois desenhos de tamanhos
/// diferentes empilhados no mesmo instante.
///
/// ============================ POR QUE ELE NAO E NENHUM DOS TRES PALCOS QUE JA EXISTEM ============================
/// O defeito do dono e um INSTANTE, e nenhum dos palcos anteriores consegue olhar pra um instante:
///
///   * `--biovivo` (a escada em retrato) fotografa UM quadro por degrau e existe justamente pra **nao**
///     ter cena rodando -- ele marca `EstreiaVista` no nascimento pra pular todas as cinematicas;
///   * `--bioolhar` (os tres pedidos visuais) fotografa **um** instante no meio da metamorfose, e a
///     pergunta dele e sobre uma CAMADA (a silhueta acende?), nao sobre a ORDEM entre duas camadas;
///   * `--diagcena` filma a cena inteira, mas so a do CORPO LOCAL e so pelas formas do catalogo -- e o
///     corpo do bio nao vem de `FormaDef.Corpo`, vem da FICHA (`Appearance.Corpo` -> `S2C.PeerLook`),
///     que e um caminho de rede que o corpo local nunca exercita sozinho.
///
/// Uma foto so nao pega o defeito: ela pega um estado. **Quem responde "antes do fim" e a SEQUENCIA** --
/// e por isso este palco existe pra um robo que tira ~30 fotos por cena, uma por segundo, e depois
/// procura o instante em que o desenho do corpo trocou.
///
/// ============================ QUATRO CORPOS, E CADA UM RESPONDE UMA PERGUNTA ============================
///   F -- o BIO que roda a metamorfose de 28,0 s inteira. **O filme principal**: em nenhum dos ~30
///        quadros pode haver corpo meio trocado, e o corpo novo so pode entrar na virada.
///   M -- um SAIYAJIN que vira OOZARU. Ele existe pra provar que a regra e **GENERICA** e nao um `if`
///        de bio: o Oozaru troca o corpo por outro caminho (`FormaDef.Corpo`, lido so pelo cliente
///        dentro do `Vestir`) e mesmo assim tem que trocar no MESMO beat -- o `Efeito.Assumir`.
///   K -- um BIO IDENTICO ao F que leva um NOCAUTE no meio da propria cena. A pergunta do dono:
///        **que corpo ficou?**
///   D -- um BIO IDENTICO ao F que roda a cena com o **DEFEITO INJETADO** pelo cliente
///        (`World.VestirNaHoraDeTeste`, que e literalmente o codigo de antes do conserto). Sem ele a
///        bancada inteira e uma lista de afirmacoes que ninguem provou serem capazes de ficar
///        vermelhas -- e uma bancada que nao sabe reprovar nao aprova nada.
/// ==========================================================================================================
///
/// ============================ ELE NAO ENCURTA CAMINHO ============================
/// Os corpos nascem pelo `NascerNpc` de producao, viram bio pelo `NascerBioAndroide` de producao (com
/// laboratorio e fornada de verdade), sobem degrau pelo `SubirDegrauDoBio` de producao -- que e quem
/// dispara a cena --, viram macaco pelo `VirarFera` de producao (o mesmo funil do `admin_forma`) e caem
/// pelo `CombatState.Nocautear` de producao. O unico atalho e o RELOGIO.
///
/// A PORTA DA LUA NAO E EXERCITADA AQUI de proposito: quem cobra rabo, genoma, fase e linhagem e o
/// `--luaferateste` (doze familias de provas). O que este palco filma e a ORDEM DO DESENHO, e para ela
/// tanto faz por que porta a fera entrou -- e mandar a noite cair apagaria as fotos do bio, que sao a
/// razao de o palco existir.
/// ================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>`--biofilme`. Ver o cabecalho.</summary>
	private bool _bioFilme;

	/// <summary>Os quatro corpos em cena. Zero = o palco ainda nao foi montado.</summary>
	private int _filmeF, _filmeM, _filmeK, _filmeD;

	/// <summary>Em que passo ele esta. -1 = montado e ainda sem anunciar nada.</summary>
	private int _filmePasso;

	/// <summary>Quando dar o proximo passo (ms). Zero = acabou.</summary>
	private long _filmeProximo;

	/// <summary>A conta de quem esta OLHANDO -- os corpos se plantam em volta dela.</summary>
	private string _filmeConta = "";

	/// <summary>O elenco, anunciado POR ID -- ver a mesma marca do `--bioolhar`.</summary>
	public const string MarcaDoElencoDoFilme = "[FILME-DO-BIO] elenco ";

	/// <summary>O prefixo do anuncio de passo. Ver <see cref="TickDoFilmeDoBio"/>.</summary>
	public const string MarcaDoFilmeDoBio = "[FILME-DO-BIO] passo ";

	/// <summary>
	/// QUANTO DURA CADA PASSO, e eles NAO sao iguais -- tres deles carregam uma cinematica inteira.
	///
	/// A metamorfose do bio mede 28,0 s ate a virada e 31,0 s de cena (`BioSemiPerfeito`), e o robo
	/// precisa filmar ALEM da virada -- e bem alem. O motivo saiu de uma rodada: a virada e o mesmo beat
	/// que abre a CRATERA, e a nuvem de poeira dela cobre o boneco por varios segundos, entao a tira que
	/// devia mostrar "corpo velho / corpo novo" saiu com o segundo lado dentro de uma nuvem. O veredito
	/// estava certo (o sprite trocou entre dois quadros, medido) e a IMAGEM nao mostrava nada.
	///
	/// Quarenta e oito segundos dao a cena, a poeira e a folga do veredito. A do Oozaru mede 6,2 s e
	/// nao abre cratera nenhuma (o dono foi explicito), entao catorze bastam.
	///
	/// O passo 5 e longo por outro motivo: ele espera o filme do K chegar ao fim (42 s contados do passo
	/// 4), que e a unica maneira de responder "e depois, que corpo ficou?".
	/// </summary>
	private static readonly double[] SegundosDoPassoDoFilme = [8, 10, 48, 14, 14, 34, 48, 8];

	/// <summary>
	/// O BP DO DOADOR DE MENTIRA -- o mesmo dos outros dois palcos do bio, e pelo mesmo motivo: a
	/// criatura nasce com METADE do doador mais forte.
	/// </summary>
	private const double BpDoDoadorDoFilme = 4e9;

	/// <summary>
	/// ONDE OS QUATRO SE PLANTAM, EM TILES DO HOST.
	///
	/// CURTO E EM DOIS EIXOS, pelo numero que o `--bioolhar` ja calculou por escrito: a camera segue o
	/// host e o zoom padrao e 3x, entao a seis tiles um corpo ja esta a 576 px do centro da tela. Os
	/// quatro ficam em colunas diferentes porque o robo fotografa UM POR VEZ e cada recorte tem que
	/// pegar so o seu -- corpo empilhado no recorte ja custou uma rodada inteira ao `RoboDeDoisCorpos`.
	///
	/// O OOZARU FICA MAIS LONGE (5 tiles) porque ele e um macaco de dez metros: no tile 3 ele entraria
	/// no recorte do vizinho.
	/// </summary>
	private static readonly (int X, int Y) TileF = (3, -3), TileM = (5, 0), TileK = (3, +3), TileD = (-3, 0);

	/// <summary>
	/// MONTA O PALCO: quatro corpos em volta de quem entrou, todos ainda gente.
	/// </summary>
	private void MontarOFilmeDoBio(ServerPlayer pl)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) { GD.PrintErr("[biofilme] a zona do host nao tem mapa"); return; }

		_filmeConta = pl.Conta;
		_filmeF = PlantarCorpo(pl, mapa, TileF.X, TileF.Y, "Filme F");
		_filmeK = PlantarCorpo(pl, mapa, TileK.X, TileK.Y, "Filme K");
		_filmeD = PlantarCorpo(pl, mapa, TileD.X, TileD.Y, "Filme D");
		_filmeM = PlantarSaiyajinDoFilme(pl, mapa);

		if (_filmeF == 0 || _filmeM == 0 || _filmeK == 0 || _filmeD == 0)
		{ GD.PrintErr("[biofilme] um dos quatro corpos nao nasceu -- o palco nao sobe"); return; }

		_filmePasso = -1;
		_filmeProximo = NowMs() + 6_000;

		AnunciarNoMundo($"{MarcaDoElencoDoFilme}{_filmeF},{_filmeM},{_filmeK},{_filmeD}");
		GD.Print($"[biofilme] elenco: F={_filmeF} (o filme), M={_filmeM} (Oozaru), "
			   + $"K={_filmeK} (nocaute no meio), D={_filmeD} (defeito injetado). O roteiro comeca em 6 s.");
	}

	/// <summary>
	/// O SAIYAJIN DO OOZARU, pelo molde `guardiao_saiyajin` de producao -- e o molde importa: a fera
	/// cai sozinha sem RABO INTEIRO (`TickDoOozaru` -> `TemRaboInteiro`), e um cidadao qualquer nao tem.
	/// </summary>
	private int PlantarSaiyajinDoFilme(ServerPlayer pl, ZoneCollision mapa)
	{
		Vec2 onde = mapa.PontoLivrePerto(
			pl.Pos + new Vec2(TileM.X * ZoneCollision.TileSize, TileM.Y * ZoneCollision.TileSize));
		ServerPlayer? s = NascerNpc("guardiao_saiyajin", pl.Zone, onde, ++_lugarDaBancadaDeBio);
		if (s == null) return 0;

		// PARADO, pelo motivo escrito no `--biovivo`: o robo compara recortes pixel a pixel, e um corpo
		// que anda entre dois quadros troca o FUNDO -- a diferenca sai grande por causa do mato atras
		// dele, e a bancada leria isso como "o corpo trocou".
		s.Cerebro = null;
		s.Name = "Filme M";
		s.Conta = "filme-do-bio";
		s.SigAtributos = "";
		TrocarAparencias(s);
		return s.Id;
	}

	/// <summary>
	/// UM PASSO POR VOLTA. Roda no tique de 1 Hz, ao lado dos outros dois palcos do bio.
	///
	/// ============================ OS OITO PASSOS ============================
	///   0  CONTROLE     -- os quatro ainda sao gente. Sem ele, "o corpo trocou" nao teria de que;
	///   1  NASCIMENTO   -- F, K e D viram bio e rompem a carapaca (larva -> imperfeito);
	///   2  **O FILME**  -- F entra na metamorfose de 28,0 s. E este o pedido do dono;
	///   3  O OOZARU     -- M vira macaco. A prova de que a regra nao e um `if` de bio;
	///   4  A CENA DO K  -- K entra na MESMA metamorfose;
	///   5  O NOCAUTE    -- K cai no meio dela. **Que corpo ficou?**;
	///   6  O DEFEITO    -- D roda a cena com o cliente na versao de ANTES do conserto;
	///   7  FIM.
	/// =======================================================================
	/// </summary>
	private void TickDoFilmeDoBio()
	{
		if (_filmeProximo == 0 || NowMs() < _filmeProximo) return;
		if (!_players.TryGetValue(_filmeF, out ServerPlayer? f)
			|| !_players.TryGetValue(_filmeM, out ServerPlayer? m)
			|| !_players.TryGetValue(_filmeK, out ServerPlayer? k)
			|| !_players.TryGetValue(_filmeD, out ServerPlayer? d))
		{ _filmeProximo = 0; GD.PrintErr("[biofilme] um corpo do palco sumiu"); return; }

		int passo = ++_filmePasso;
		if (passo >= SegundosDoPassoDoFilme.Length)
		{
			_filmeProximo = 0;
			GD.Print("[biofilme] o roteiro terminou.");
			return;
		}
		_filmeProximo = NowMs() + (long)(SegundosDoPassoDoFilme[passo] * 1000);

		// O PALCO SEGUE O ELENCO, MAS SO NA VIRADA DO PASSO -- copiado do `--bioolhar` e pelo motivo
		// escrito la: as medidas sao pares de quadros CONSECUTIVOS, e um corpo que se teleporta entre
		// os dois faz o fundo inteiro trocar. Reposicionar so na virada deixa a janela limpa.
		ServerPlayer? olheiro = _players.Values.FirstOrDefault(
			p => p.Conta == _filmeConta && p.Peer != null && p.Zone.Equals(f.Zone));
		if (olheiro != null)
		{
			int t = ZoneCollision.TileSize;
			f.Pos = olheiro.Pos + new Vec2(TileF.X * t, TileF.Y * t);
			m.Pos = olheiro.Pos + new Vec2(TileM.X * t, TileM.Y * t);
			k.Pos = olheiro.Pos + new Vec2(TileK.X * t, TileK.Y * t);
			d.Pos = olheiro.Pos + new Vec2(TileD.X * t, TileD.Y * t);
		}
		else GD.PrintErr("[biofilme] o olheiro nao esta na zona -- as fotos vao sair vazias");

		switch (passo)
		{
			case 0: break;   // o CONTROLE

			// ============================ OS TRES BIOS NASCEM IGUAIS, E ISSO E O QUE OS TORNA COMPARAVEIS ============================
			// O F, o K e o D passam pelo MESMO nascimento e pelo MESMO degrau. A unica coisa que os
			// separa e o que acontece DEPOIS -- um roda limpo, um leva nocaute, um roda com o defeito
			// posto a mao no cliente. Tres cenas com a mesma largada e tres fins diferentes.
			//
			// E O ROMPIMENTO DA CARAPACA SAI AQUI, junto: ele NAO e uma `Cinematica` (o DM tem duas
			// linhas de `flick`, 0,6 s, ver `CenaBio.Rompimento`), entao ele nao entra no filme -- e
			// deixa-lo pro passo 2 poria um piscar de silhueta em cima da largada da metamorfose.
			// ==========================================================================================================================
			case 1:
				NascerNoFilme(f);
				NascerNoFilme(k);
				NascerNoFilme(d);
				SubirDegrauDoBio(f, BioAndroids.Imperfeito);
				SubirDegrauDoBio(k, BioAndroids.Imperfeito);
				SubirDegrauDoBio(d, BioAndroids.Imperfeito);
				GD.Print($"[biofilme] passo 1: os tres bios estao no degrau {f.Ficha.bio_stage} "
					   + $"(imperfeito), corpo {f.Visual.Corpo}");
				break;

			// ============================ **O FILME** ============================
			// O `SubirDegrauDoBio` e a porta de producao e e ele quem dispara a `CenaDoBio`. Nada e
			// empurrado aqui: o servidor escreve o degrau, o BP e o `Visual.Corpo` AGORA (ele e
			// autoridade e nao espera cena nenhuma) e manda os dois pacotes na ordem que ele ja manda.
			// ====================================================================
			case 2:
				SubirDegrauDoBio(f, BioAndroids.SemiPerfeito);
				GD.Print($"[biofilme] passo 2: **O FILME** -- F na metamorfose de 28,0 s, "
					   + $"corpo da ficha agora = {f.Visual.Corpo}, preso por {f.CenaSegundos:0.#}s");
				break;

			// ============================ O OOZARU, E ELE E O CONTRA-EXEMPLO QUE PROVA A REGRA ============================
			// O bio troca o corpo pela FICHA (`Appearance.Corpo` -> `S2C.PeerLook`); o Oozaru troca pelo
			// CATALOGO (`FormaDef.Corpo`, que so o cliente le, e so de dentro do `Vestir`). Sao dois
			// caminhos de codigo diferentes chegando no mesmo pixel -- e a regra do dono ("o corpo troca
			// no FIM") tem que valer nos dois, senao ela e um `if` de bio disfarcado de regra.
			//
			// `VirarFera` e o funil de producao (o mesmo que o `admin_forma` usa), e nao a porta da lua:
			// ver o cabecalho sobre por que a noite nao cai aqui.
			// ========================================================================================================
			case 3:
				VirarFera(m, FormaOozaru.Regular);
				GD.Print($"[biofilme] passo 3: O OOZARU -- M em {m.Oozaru}, cena de 6,2 s");
				break;

			case 4:
				SubirDegrauDoBio(k, BioAndroids.SemiPerfeito);
				GD.Print($"[biofilme] passo 4: a cena do K comecou (a que vai ser interrompida)");
				break;

			// ============================ O NOCAUTE NO MEIO DA CENA ============================
			// Catorze segundos depois da largada -- ou seja EXATAMENTE no meio dos 28,0 s, depois dos
			// feixes de chao (16,0 s) e bem antes da virada.
			//
			// PELA PORTA DE PRODUCAO (`CombatState.Nocautear`), que e a mesma que o `MeleeResolver`
			// chama quando um nucleo vital quebra. Escrever `F.KO = true` aqui testaria o campo e nao o
			// nocaute -- e o `Nocautear` faz mais tres coisas (derruba a guarda, zera o contra e marca o
			// prazo) que sao justamente as que mudam o que o corpo DESENHA.
			// ==================================================================================
			case 5:
				k.Facing = Facing.East; k.FacingDaQueda = Facing.East;
				// `porVital: false`: o corpo do palco esta INTEIRO -- este nocaute e encenacao, e nao um
				// nucleo cedendo. Com `true` o `CombatState.Tick` o levantaria no quadro seguinte, porque
				// ele pergunta ao corpo e o corpo esta bem.
				k.Combate?.Nocautear(MeleeResolver.TetoDoNocaute, porVital: false);
				k.SigAtributos = "";
				GD.Print($"[biofilme] passo 5: O NOCAUTE -- K KO={k.Ficha.KO} no meio da propria cena "
					   + $"(faltam ~14 s pra virada), corpo da ficha = {k.Visual.Corpo}");
				break;

			// ============================ O DEFEITO INJETADO ============================
			// Quem liga o defeito e o CLIENTE (`World.VestirNaHoraDeTeste`), no fim do passo 5 -- ele ja
			// esta ligado quando este `SubirDegrauDoBio` sai. O palco nao muda uma linha: e a mesma
			// chamada do passo 2, no mesmo corpo-molde, e a unica diferenca esta do outro lado do fio.
			// ===========================================================================
			case 6:
				SubirDegrauDoBio(d, BioAndroids.SemiPerfeito);
				GD.Print($"[biofilme] passo 6: O DEFEITO INJETADO -- D na mesma metamorfose do passo 2");
				break;

			case 7:
				GD.Print($"[biofilme] passo 7: FIM -- F corpo {f.Visual.Corpo} degrau {f.Ficha.bio_stage} | "
					   + $"K corpo {k.Visual.Corpo} KO={k.Ficha.KO} | D corpo {d.Visual.Corpo} | "
					   + $"M {m.Oozaru}");
				break;
		}

		// O ELENCO SAI DE NOVO A CADA PASSO -- ver o mesmo bloco no `--bioolhar`: anunciado uma vez so,
		// dentro do login do host, ele nunca chega (o robo assina o canal depois).
		AnunciarNoMundo($"{MarcaDoElencoDoFilme}{_filmeF},{_filmeM},{_filmeK},{_filmeD}");

		// O ANUNCIO VEM DEPOIS DA ACAO, pelo motivo que os outros dois palcos ja pagaram: anunciando
		// antes, o robo filma o estado velho com o rotulo novo e a rodada inteira sai deslocada em um.
		AnunciarNoMundo($"{MarcaDoFilmeDoBio}{passo}");
	}

	/// <summary>
	/// O NASCIMENTO, PELA PORTA DE PRODUCAO -- laboratorio e fornada de verdade, como nos outros dois
	/// palcos. COM DNA Saiyajin, que aqui e indiferente (nenhum passo pede SSJ) mas mantem os tres
	/// bios identicos ao corpo A do `--bioolhar` -- corpos de bancada que divergem calados entre
	/// bancadas sao a maneira mais barata de duas medidas discordarem sem que ninguem saiba por que.
	/// </summary>
	private void NascerNoFilme(ServerPlayer s)
	{
		var lab = new Obra
		{
			Id = 984_960 + s.Id % 30, Tipo = "Android_Creation_Mainframe", Aparafusada = true, Lab = 2,
			X = s.Pos.X, Y = s.Pos.Y, DonoConta = s.Conta, DonoNome = s.Name,
		};
		lab.PorZona(s.Zone);
		_noChao.Add(lab);

		var g = new Gestacao
		{
			DonoConta = s.Conta,
			MaiorBp = BpDoDoadorDoFilme,
			TemSaiyajin = true,
			Amostras = { new Amostra { Raca = "Sayian", Doador = "doador do filme", Bp = BpDoDoadorDoFilme } },
		};

		NascerBioAndroide(s, lab, g);
	}
}
