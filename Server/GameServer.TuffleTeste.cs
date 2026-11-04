using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Races;
using Jandirus.Core.Tech;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--tuffleteste` -- O NEO TUFFLE (2026-10-18). O pedido inteiro esta no cabecalho de `GameServer.NeoTuffle.cs`. Os tres
/// personagens entram pela porta (clientes crus): o Baby (Tsufurujin), o Vegeta (Saiyajin) e a Bulma (Humana).
///
///   A. ANTES DA MAQUINA: o Tsufurujin comum nao possui ninguem -- e o CONTRA-EXEMPLO `PosseSemAMaquinaDeTeste` (a maquina nao serviria pra
///      nada).
///   M. A MAQUINA: o menu do mainframe (o laboratorio e a conversao nas duas moedas), o Neo Tuffle Lab que so o Tsufurujin instala (com a
///      tecnologia do laboratorio), a conversao que so o Tsufurujin faz e que cobra, o disco e o verb que acende -- e (2026-10-20) o corpo que
///      vira o do Baby no pacote de aparencia (CONTRA-EXEMPLO `TuffleSemCorpoDoBabyDeTeste`) -- e (2026-10-21) o Neo Tuffle Lab que vai pro
///      pacote das construcoes no desenho do Cell Pod (CONTRA-EXEMPLO `ArteDoLaboratorio.LabSemCellPodDeTeste`).
///   P. A POSSE: as recusas (de pe, longe); a entrada (o poder e os stats do Vegeta, o corpo levantado, o lugar dele); o que o mundo ve (o
///      nome, a raca e o cabelo branco-acinzentado -- CONTRA-EXEMPLO `TuffleSemCabeloBrancoDeTeste`); o corpo de verdade do Vegeta escondido,
///      fora da grade, intocavel e fora das buscas de quem pega um nocauteado (CONTRA-EXEMPLO `HospedeAVistaDeTeste`), andando com o Tuffle; o
///      dono dele sem as redeas -- a guarda que ele manda pelo fio e engolida (CONTRA-EXEMPLO `HospedeComAsRedeasDeTeste`); o Tuffle fora do
///      disco enquanto esta dentro; e o verb apagado (ninguem possui duas vezes).
///   F. O FIM: o corpo possuido cai e o Tuffle e expulso -- de volta ao proprio corpo, um tile ao lado --, e o Vegeta volta a ser corpo onde o
///      emprestado caiu, nocauteado e com a perna que o emprestado perdeu (CONTRA-EXEMPLO `PosseNaoAcabaNoNocauteDeTeste`).
///   S. A SAIDA DO JOGO no meio da posse, pelos dois lados: o que vai pro disco e a pessoa.
///   D. A MORTE do corpo possuido e a do Vegeta, pelas regras dele.
/// </summary>
public sealed partial class GameServer
{
	private bool _tuffleJaRodou;
	private int _tfOk, _tfFalhou;

	/// <summary>O comeco dos ids das maquinas que esta bancada poe no chao.</summary>
	private const int IdDoPalcoDoTuffle = 986_100;

	// (2026-10-29, o pedido 159) o Goku e o hospede da familia H (a morte da copia): o Vegeta e a Bulma ja morreram nas de cima
	// (2026-10-30, o pedido 180) e a Chichi e a hospede do logout dentro da copia (familia L)
	private static readonly string[] ContasDaBancadaDoTuffle =
		["bancada_tuffle_baby", "bancada_tuffle_vegeta", "bancada_tuffle_bulma", "bancada_tuffle_goku", "bancada_tuffle_chichi"];

	private void AfirmarTf(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _tfOk++; GD.Print($"[tuffle]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _tfFalhou++; GD.PrintErr($"[tuffle]   FALHA {nome}   [{detalhe}]"); }
	}

	private void ApagarContasDoTuffle()
	{
		if (_store == null) return;
		foreach (string conta in ContasDaBancadaDoTuffle)
			try
			{
				string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
				if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[tuffle] nao apaguei '{conta}': {e.Message}"); }
	}

	public void RodarBancadaDoTuffle(int porta)
	{
		if (_tuffleJaRodou) return;
		_tuffleJaRodou = true;
		_tfOk = _tfFalhou = 0;
		GD.Print("[tuffle] ================ O NEO TUFFLE (dono, 2026-10-18) ================");

		const float T = ZoneCollision.TileSize;
		ApagarContasDoTuffle();
		var c = new ClienteCruDeTeste[ContasDaBancadaDoTuffle.Length];
		for (int i = 0; i < c.Length; i++) c[i] = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, c);
		try
		{
			// ------------------------------------------------------------ as pecas
			CharacterDraft Ficha(string nome, string raca, string genero) => new()
			{
				Name = nome, Race = raca, Planet = SorteioDeRaca.PlanetaDaRaca(raca), Gender = genero, Age = 18,
				ChosenClass = CharacterDraft.EscolhasDeClasse(raca) is { Length: > 0 } linhas ? linhas[0] : "",
				Porte = "Medium", Backstory = "Personagem da bancada do Neo Tuffle, criado por um cliente cru.",
			};
			ServerPlayer? Entrar(int i, CharacterDraft ficha)
			{
				if (!mesa.Entra(c[i]) || !mesa.Loga(c[i], ContasDaBancadaDoTuffle[i])) return null;
				c[i].CriarPersonagem(0, ficha);
				return mesa.Bombeia(() => c[i].NoMundo || c[i].Recusa != null, 8000) && c[i].NoMundo ? mesa.CorpoDe(ContasDaBancadaDoTuffle[i]) : null;
			}
			// O NOCAUTEADO DO LADO DE QUEM VAI POSSUIR: na mesma zona, um tile a leste, os dois de frente.
			void LadoALado(ServerPlayer a, ServerPlayer b)
			{
				if (b.Zone.Hash != a.Zone.Hash) MoveToZone(b.Id, a.Zone, a.Pos + new Vec2(T, 0));
				b.Pos = a.Pos + new Vec2(T, 0);
				a.Facing = Facing.East;
				b.Facing = Facing.West;
			}
			bool VeOVerb(ServerPlayer quem, string chave) => RequisitosDosVerbos.Linha(chave) is { } l && l.Requisito.Atende(PosseDe(quem));
			Obra NoChao(ServerPlayer aosPesDe, int n)
			{
				var o = new Obra
				{
					Id = IdDoPalcoDoTuffle + n, Tipo = "Android_Creation_Mainframe", Aparafusada = true,
					X = aosPesDe.Pos.X, Y = aosPesDe.Pos.Y, DonoConta = aosPesDe.Conta, DonoNome = aosPesDe.Name,
				};
				o.PorZona(aosPesDe.Zone);
				_noChao.Add(o);
				return o;
			}
			// O PACOTE DE APARENCIA LIDO COMO O CLIENTE O LE (`GameClient`, no `case S2C.PeerLook`): o nome, a raca, o segundo byte de marcas
			// (o bit 1 e o cabelo do Neo Tuffle; o bit 2, de 2026-10-20, o corpo do Baby) e os bytes que sobraram.
			(string Nome, string Raca, bool CabeloBranco, bool CorpoDoBaby, int Sobra) Look(ServerPlayer p)
			{
				var fio = new NetDataReader(PacoteDeAparencia(p).CopyData());
				fio.GetByte();
				fio.GetInt();
				string nome = fio.GetString(Protocol.MaxNomeNoFio), raca = fio.GetString(24);
				fio.GetString(8);
				fio.GetAppearance();
				fio.GetByte(); fio.GetByte(); fio.GetString(120);
				byte marcas2 = fio.GetByte();
				return (nome, raca, (marcas2 & 2) != 0, (marcas2 & 4) != 0, fio.AvailableBytes);
			}
			ServerPlayer? Possuir(ServerPlayer tuffle, out List<string> ouvi)
			{
				ouvi = Ouvir(() => UsarHabilidade(tuffle, "tuffle_possuir"));
				return PosseDoTuffleDe(tuffle.Id)?.Hospede;
			}

			ServerPlayer? baby = Entrar(0, Ficha("Baby", "Tsujin", "Male"));
			ServerPlayer? vegeta = Entrar(1, Ficha("Vegeta", "Saiyan", "Male"));
			ServerPlayer? bulma = Entrar(2, Ficha("Bulma", "Human", "Female"));
			AfirmarTf("(montagem) o Baby (Tsufurujin), o Vegeta (Saiyajin) e a Bulma (Humana) entram pela porta",
				baby != null && vegeta != null && bulma != null, $"{c[0].Recusa} | {c[1].Recusa} | {c[2].Recusa}");
			if (baby == null || vegeta == null || bulma == null) return;
			LadoALado(baby, vegeta);
			if (bulma.Zone.Hash != baby.Zone.Hash) MoveToZone(bulma.Id, baby.Zone, baby.Pos - new Vec2(4 * T, 0));
			bulma.Pos = baby.Pos - new Vec2(4 * T, 0);
			baby.Ficha.BP = 1_000;
			vegeta.Ficha.BP = 5_000_000;
			baby.Ficha.Statify();
			vegeta.Ficha.Statify();

			// ============================================================ A) ANTES DA MAQUINA
			GD.Print("[tuffle] -- A) antes da maquina: o Tsufurujin comum nao possui ninguem --");
			vegeta.Combate.Nocautear(600);
			Possuir(baby, out List<string> comum);
			AfirmarTf("O TSUFURUJIN COMUM NAO POSSUI: com o Vegeta nocauteado do lado, o aperto diz que ele nao e um Neo Tuffle -- e nada muda",
				PosseDoTuffleDe(baby.Id) == null && comum.Any(x => x.Contains("não é um Neo Tuffle")), Ultimos(comum));
			PosseSemAMaquinaDeTeste = true;
			try { Possuir(baby, out _); }
			finally { PosseSemAMaquinaDeTeste = false; }
			AfirmarTf("CONTRA-EXEMPLO (defeito injetado: o Tsufurujin comum possui como o Neo Tuffle): ele entra no Vegeta -- a regua da maquina REPROVA",
				PosseDoTuffleDe(baby.Id)?.Hospede == vegeta);
			SoltarDaPosse(baby);
			vegeta.Combate.Levantar();

			// ============================================================ M) A MAQUINA
			GD.Print("[tuffle] -- M) a maquina: o Neo Tuffle Lab e a conversao --");
			// (2026-10-25) O BUG DO DONO: "tsufurujin n ta conseguindo contruir maquina de fazer neotuffle, n aparece no menu de tech". O Neo
			// Tuffle Lab se instala num mainframe, e o catalogo so deixava o Humano construir um (`CatalogoDeObras.RacasDoMainframe`).
			bool NaAbaTech(CatalogoDeObras cat, ServerPlayer p) => cat.Ofertas(p.Ficha, p.Race).Any(o => o.Obra.Id == CatalogoDeObras.Mainframe);
			AfirmarTf("**O MAINFRAME ESTA NA ABA TECH DO TSUFURUJIN** (o bug de 2026-10-25: sem ele nao ha Neo Tuffle Lab): a oferta e linha pro Baby e pra Bulma (Humana) -- e pro Vegeta (Saiyajin) continua nao sendo",
				_obras != null && NaAbaTech(_obras, baby) && NaAbaTech(_obras, bulma) && !NaAbaTech(_obras, vegeta));
			CatalogoDeObras doDm;
			CatalogoDeObras.MainframeSoDoHumanoDeTeste = true;
			try { doDm = CatalogoDeObras.Parse(Godot.FileAccess.GetFileAsString("res://Assets/Data/construcoes.json")); }
			finally { CatalogoDeObras.MainframeSoDoHumanoDeTeste = false; }
			AfirmarTf("CONTRA-EXEMPLO (defeito injetado: o catalogo com as racas do DM): o Baby nao ve o mainframe na aba Tech -- a regua de cima REPROVA",
				!NaAbaTech(doDm, baby) && NaAbaTech(doDm, bulma));

			Interacoes.Acao[] labs = Interacoes.De("Android_Creation_Mainframe/labs");
			Interacoes.Acao[] conversao = Interacoes.De("Android_Creation_Mainframe/tuffle");
			AfirmarTf("O MENU DO MAINFRAME: o Neo Tuffle Lab entre os laboratorios, e a conversao nas duas moedas -- um milhao de zeni ou o dobro em recursos",
				labs.Any(a => a.Verbo == "lab_tuffle") && conversao.Length == 2 && conversao.All(a => a.Verbo == "virar_tuffle")
				&& conversao[0].Rotulo.Contains("1.000.000 zeni") && conversao[1].Rotulo.Contains("2.000.000 recursos") && CustoNeoTuffle == 1_000_000
				&& Interacoes.Aceita("Android_Creation_Mainframe", "virar_tuffle") && Interacoes.Aceita("Android_Creation_Mainframe", "lab_tuffle"),
				string.Join(" | ", conversao.Select(a => a.Rotulo)));
			// AS LINHAS DA TABELA DOS VERBOS e que tiram o laboratorio e as duas conversoes da tela de quem nao e Tsufurujin: sem elas o menu E
			// mostraria os tres botoes pra todo mundo, o servidor recusaria do mesmo jeito, e nenhuma outra regua veria a falta (a contraprova
			// por mutacao de 2026-10-19 achou).
			Requisito[] doTsufurujin =
				[RequisitosDosVerbos.DoVerbo("lab_tuffle"), RequisitosDosVerbos.DoVerbo("virar_tuffle"),
				 RequisitosDosVerbos.DoVerbo("virar_tuffle", Recurso.PalavraDeRecursos)];
			AfirmarTf("...E SO PRO TSUFURUJIN: a tabela dos verbos esconde o Neo Tuffle Lab e as duas conversoes da Bulma (Humana) e as mostra pro Baby",
				doTsufurujin.All(r => !r.Livre && r.Atende(PosseDe(baby)) && !r.Atende(PosseDe(bulma))),
				string.Join(" | ", doTsufurujin.Select(r => r.Texto)));

			Obra daBulma = NoChao(bulma, 1);
			bulma.Ficha.techskill = 80;
			List<string> humana = Ouvir(() => ComandoDeTech(bulma, "lab_tuffle", ""));
			AfirmarTf("O NEO TUFFLE LAB E DO TSUFURUJIN: a Bulma (Humana, com tecnologia) ouve que ele pede um Tsufurujin, e o mainframe continua cru",
				daBulma.Lab == 0 && humana.Any(x => x.Contains("pede um Tsufurujin")), Ultimos(humana));

			Obra doBaby = NoChao(baby, 2);
			baby.Ficha.techskill = 10;
			List<string> semTech = Ouvir(() => ComandoDeTech(baby, "lab_tuffle", ""));
			List<string> androide = Ouvir(() => ComandoDeTech(baby, "lab_androide", ""));
			AfirmarTf("...com a tecnologia do laboratorio (70): sem ela o Baby nao instala -- e o Android Lab continua sendo dos humanos",
				doBaby.Lab == 0 && semTech.Any(x => x.Contains("tecnologia")) && androide.Any(x => x.Contains("pede um humano")),
				$"{Ultimos(semTech)} || {Ultimos(androide)}");
			baby.Ficha.techskill = 80;
			var arteCrua = ArteDaObra(doBaby, _obras?.Get(doBaby.Tipo));
			List<string> instalou = Ouvir(() => ComandoDeTech(baby, "lab_tuffle", ""));
			AfirmarTf("...e com ela o mainframe vira um Neo Tuffle Lab (o laboratorio 3)",
				doBaby.Lab == LabDoNeoTuffle && instalou.Any(x => x.Contains("Neo Tuffle Lab")), Ultimos(instalou));
			// (2026-10-21) O DESENHO DO LAB: o pacote das construcoes manda o Cell Pod pro Neo Tuffle Lab (o pedido do dono, `ArteDoLaboratorio`)
			// -- e o mainframe cru, antes da instalacao, ia no do catalogo. Os caminhos estao escritos aqui de novo, e nao lidos das constantes.
			var arteDoLab = ArteDaObra(doBaby, _obras?.Get(doBaby.Tipo));
			AfirmarTf("O NEO TUFFLE LAB VIRA O CELL POD: cru, o mainframe ia no desenho do catalogo (`64x64tech`, `android_pod`); instalado o "
				+ "laboratorio 3, o pacote das construcoes manda a folha do Cell Pod, a animacao `default` e o -8 que centra o pod de 48 px no tile",
				arteCrua is ("res://Assets/Sprites/Misc/Objects/Technology/64x64tech.tres", "android_pod", 0f, 0f)
				&& arteDoLab is ("res://Assets/Sprites/DU/Items/Cell Pod.tres", "default", -8f, 0f),
				$"cru {arteCrua}; com o lab {arteDoLab}");
			Jandirus.Core.Tech.ArteDoLaboratorio.LabSemCellPodDeTeste = true;
			(string Arte, string Estado, float PixelX, float PixelY) semPod;
			try { semPod = ArteDaObra(doBaby, _obras?.Get(doBaby.Tipo)); }
			finally { Jandirus.Core.Tech.ArteDoLaboratorio.LabSemCellPodDeTeste = false; }
			AfirmarTf("CONTRA-EXEMPLO (defeito injetado: nenhum laboratorio troca o desenho): o Neo Tuffle Lab sai no desenho cru -- a regua do "
				+ "Cell Pod REPROVA", semPod is not ("res://Assets/Sprites/DU/Items/Cell Pod.tres", "default", -8f, 0f), $"{semPod}");

			bulma.Pos = baby.Pos;
			List<string> bulmaConverte = Ouvir(() => ComandoDeTech(bulma, "virar_tuffle", ""));
			bulma.Pos = baby.Pos - new Vec2(4 * T, 0);
			baby.Ficha.Zeni = 0;
			List<string> semZeni = Ouvir(() => ComandoDeTech(baby, "virar_tuffle", ""));
			AfirmarTf("A CONVERSAO E DO TSUFURUJIN E COBRA: a Humana dentro do lab ouve que a maquina e pro corpo de um Tsufurujin; o Baby sem zeni nao vira",
				!bulma.Ficha.NeoTuffle && bulmaConverte.Any(x => x.Contains("Tsufurujin")) && !baby.Ficha.NeoTuffle && semZeni.Count > 0,
				$"{Ultimos(bulmaConverte)} || {Ultimos(semZeni)}");
			bool viaOVerbAntes = VeOVerb(baby, "hab:tuffle_possuir");
			bool corpoDoBabyAntes = Look(baby).CorpoDoBaby;
			baby.Ficha.Zeni = 1_500_000;
			List<string> virou = Ouvir(() => ComandoDeTech(baby, "virar_tuffle", ""));
			AfirmarTf("...com um milhao de zeni, o Baby vira NEO TUFFLE: a marca na ficha, o zeni descontado, e a frase que aponta o verb",
				baby.Ficha.NeoTuffle && baby.Ficha.Zeni == 500_000 && virou.Any(x => x.Contains("Possuir um nocauteado")), Ultimos(virou));
			AfirmarTf("...e vai pro disco, e o verb \"Possuir um nocauteado\" acende so pra ele (antes apagado; a Bulma nao ve)",
				mesa.NoDisco(ContasDaBancadaDoTuffle[0])?.Ficha.NeoTuffle == true && !viaOVerbAntes && VeOVerb(baby, "hab:tuffle_possuir")
				&& !VeOVerb(bulma, "hab:tuffle_possuir"));
			// (2026-10-20) O CORPO DO NEO TUFFLE: o pacote de aparencia do Baby passa a levar a marca do corpo (a folha do Baby, careca e sem nada
			// por cima, no cliente -- `Appearance.CorpoDoTuffle`). O Tsufurujin comum nao a leva, nem a Bulma.
			var convertido = Look(baby);
			AfirmarTf("...E O CORPO VIRA O DO BABY: o pacote de aparencia dele passa a levar a marca do corpo do Neo Tuffle (antes da maquina nao levava; "
				+ "a Bulma nao leva) -- o nome e a raca continuam os dele",
				!corpoDoBabyAntes && convertido is { Nome: "Baby", Raca: "Tsujin", CorpoDoBaby: true, CabeloBranco: false, Sobra: 0 } && !Look(bulma).CorpoDoBaby,
				$"antes {corpoDoBabyAntes}, depois {convertido}, Bulma {Look(bulma).CorpoDoBaby}");
			TuffleSemCorpoDoBabyDeTeste = true;
			bool semCorpo;
			try { semCorpo = !Look(baby).CorpoDoBaby; }
			finally { TuffleSemCorpoDoBabyDeTeste = false; }
			AfirmarTf("CONTRA-EXEMPLO (defeito injetado: o pacote sem a marca do corpo do Neo Tuffle): ele sai com a cara da criacao -- a regua do corpo REPROVA",
				semCorpo);
			_noChao.RemoveAll(o => o.Id >= IdDoPalcoDoTuffle && o.Id < IdDoPalcoDoTuffle + 100);

			// ============================================================ P) A POSSE
			GD.Print("[tuffle] -- P) a posse: o Neo Tuffle entra no Vegeta nocauteado --");
			// (2026-10-20) O PODER QUE O SENSE LE (`expressedBP`) SO MUDA NO TIQUE DE FICHA, a 5 Hz, e esta bancada nao bombeia o servidor desde o
			// login dos tres: se o login da Bulma acabava antes de um tique, o Vegeta seguia com o poder de quem nunca tiquou (0) e o controle
			// dizia que a Bulma nao o sentia NEM SEM A POSSE -- a r2 do `tetonome` e a r1 do `roupababy` cairam nisso. O tique de producao, aqui.
			vegeta.Ficha.Tick(agoraMs: NowMs());
			bool sentiaOVegeta = AchoAEnergia(bulma, vegeta);   // o controle do Sense: de pe e sem posse, a Bulma o sente
			Possuir(baby, out List<string> dePe);
			vegeta.Pos = baby.Pos + new Vec2(6 * T, 0);
			vegeta.Combate.Nocautear(600);
			Possuir(baby, out List<string> longe);
			AfirmarTf("AS RECUSAS: o Vegeta de pe nao e possuido, e nocauteado a seis tiles tambem nao -- o Tuffle ouve que nao ha nocauteado ao alcance",
				PosseDoTuffleDe(baby.Id) == null && dePe.Any(x => x.Contains("nenhum jogador nocauteado")) && longe.Any(x => x.Contains("nenhum jogador nocauteado")),
				$"{Ultimos(dePe)} || {Ultimos(longe)}");

			LadoALado(baby, vegeta);
			Vec2 ondeOVegetaCaiu = vegeta.Pos;
			Jandirus.Core.Stats.Fighter fichaDoBaby = baby.Ficha;
			// (2026-10-28, o pedido 144) o resto do corpo do Baby, que tem de ficar guardado enquanto ele usa o do Vegeta -- e o botao de sair, apagado aqui fora
			Jandirus.Core.Skills.SkillBook livroDoBaby = baby.Livro;
			Jandirus.Core.Forms.EstadoDeForma formaDoBaby = baby.Forma;
			bool viaOSairAntes = VeOVerb(baby, "hab:tuffle_sair");
			// ...e um corpo com o que mostrar: o Vegeta sabe a Afterimage e despertou o Super Saiyajin, e o Baby nao
			vegeta.Livro.Dar(PathDoZanzoken);
			int redeDoSsj1 = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdSsj1);
			vegeta.Forma.Liberadas.Add(redeDoSsj1);
			bool babySabiaAntes = livroDoBaby.Sabe(PathDoZanzoken) || formaDoBaby.Liberadas.Contains(redeDoSsj1);
			double bpDoDisco = mesa.NoDisco(ContasDaBancadaDoTuffle[0])?.Ficha.BP ?? -1;
			ServerPlayer? hospede = Possuir(baby, out List<string> entrou);
			AfirmarTf("A POSSE: o Neo Tuffle entra no Vegeta nocauteado e ouve que o corpo e dele ate cair",
				hospede == vegeta && entrou.Any(x => x.Contains("você entra no corpo de Vegeta")), Ultimos(entrou));
			if (hospede == null) return;
			AfirmarTf("...COM O PODER DELE: a ficha que o Baby carrega e uma copia da do Vegeta -- o BP (5.000.000) e os oito stats --, e nao a dele",
				!ReferenceEquals(baby.Ficha, fichaDoBaby) && !ReferenceEquals(baby.Ficha, vegeta.Ficha) && baby.Ficha.BP == vegeta.Ficha.BP
				&& StatsDe(baby.Ficha).SequenceEqual(StatsDe(vegeta.Ficha)),
				$"BP {baby.Ficha.BP:N0} (o dele {fichaDoBaby.BP:N0}, o do Vegeta {vegeta.Ficha.BP:N0})");
			AfirmarTf("...O CORPO LEVANTADO, no lugar do Vegeta: o nocaute sai, a vida volta, o Ki enche -- e o Baby esta onde o Vegeta caiu",
				!baby.Ficha.KO && baby.Ficha.HP >= 99 && baby.Ficha.Ki >= baby.Ficha.MaxKi - 0.5 && Vec2.Distance(baby.Pos, ondeOVegetaCaiu) < 1,
				$"KO {baby.Ficha.KO}, vida {baby.Ficha.HP:0.#}, Ki {baby.Ficha.Ki:N0}/{baby.Ficha.MaxKi:N0}, a {Vec2.Distance(baby.Pos, ondeOVegetaCaiu):0.#} px");

			// ---- (2026-10-28, o pedido 144) O CORPO DO HOSPEDE POR INTEIRO ----
			AfirmarTf("...E O RESTO DO CORPO DELE (o pedido 144: *\"poder usar todas as skills e formas desse corpo\"*): a raca e a classe sao as do "
				+ "Vegeta (Saiyajin), e o livro de tecnicas e as formas sao COPIAS dos dele -- as mesmas tecnicas, as mesmas formas despertadas -- e nao "
				+ "os do Baby",
				baby.Race == "Saiyan" && baby.Class == vegeta.Class && Perfil(baby).Raca == "Saiyan"
				&& !ReferenceEquals(baby.Livro, vegeta.Livro) && !ReferenceEquals(baby.Livro, livroDoBaby)
				&& baby.Livro.Aprendidas.Count == vegeta.Livro.Aprendidas.Count && vegeta.Livro.Aprendidas.All(baby.Livro.Sabe)
				&& !ReferenceEquals(baby.Forma, vegeta.Forma) && !ReferenceEquals(baby.Forma, formaDoBaby)
				&& baby.Forma.Liberadas.SetEquals(vegeta.Forma.Liberadas)
				&& !babySabiaAntes && baby.Livro.Sabe(PathDoZanzoken) && baby.Forma.Liberadas.Contains(redeDoSsj1),
				$"raca {baby.Race}; classe {baby.Class} (Vegeta {vegeta.Class}); tecnicas {baby.Livro.Aprendidas.Count} (Vegeta {vegeta.Livro.Aprendidas.Count}, "
				+ $"Baby {livroDoBaby.Aprendidas.Count}); a Afterimage {baby.Livro.Sabe(PathDoZanzoken)}; formas despertadas {baby.Forma.Liberadas.Count} "
				+ $"(Vegeta {vegeta.Forma.Liberadas.Count}), o SSJ {baby.Forma.Liberadas.Contains(redeDoSsj1)}; o Baby ja tinha {babySabiaAntes}");
			AfirmarAAscensao(baby);
			AfirmarTf("...E SEM A ASCENSAO DO TSUFURUJIN (*\"ele n vai ter ascencion bonus nem nada ... e se a raça do hospedeiro n tiver ascencion ele tb n "
				+ "vai ter\"*): no corpo do Saiyajin -- que tem escada de formas e nao ascende -- o bonus e 1x, com os 5.000.000 do Vegeta",
				baby.Ficha.BPBoost == 1, $"bonus {baby.Ficha.BPBoost:0.###}x");
			AfirmarTf("...E O BOTAO DE SAIR (o pedido 144): dentro do Vegeta o Baby ve \"Sair do corpo\" na aba Other -- e no proprio corpo, nao",
				VeOVerb(baby, "hab:tuffle_sair") && !viaOSairAntes, $"dentro {VeOVerb(baby, "hab:tuffle_sair")}; antes de entrar {viaOSairAntes}");

			var visto = Look(baby);
			AfirmarTf("O QUE O MUNDO VE: o corpo do Baby vai pro mundo como o Vegeta -- o nome, a raca, o corpo (sem a folha do Baby) -- e com o CABELO "
				+ "BRANCO-ACINZENTADO, sem sobrar byte",
				visto is { Nome: "Vegeta", Raca: "Saiyan", CabeloBranco: true, CorpoDoBaby: false, Sobra: 0 }, $"{visto}");
			TuffleSemCabeloBrancoDeTeste = true;
			bool semBranco;
			try { semBranco = !Look(baby).CabeloBranco; }
			finally { TuffleSemCabeloBrancoDeTeste = false; }
			AfirmarTf("CONTRA-EXEMPLO (defeito injetado: o pacote sem o cabelo do Tuffle): o corpo possuido sai com o cabelo de sempre -- a regua do pacote REPROVA",
				semBranco);

			bulma.Pos = ondeOVegetaCaiu - new Vec2(T, 0);
			bulma.Facing = Facing.East;
			ServerPlayer? naFrente = QuemEstaNaFrente(bulma, o => o.Ficha.KO && !o.Ficha.dead);
			ServerPlayer? deTecnica = AlvoDeTecnica(bulma, 5 * T, o => o.Ficha.KO && !o.Ficha.dead);
			// E MARCADO NA MIRA TAMBEM: o alvo marcado e o primeiro degrau do `AlvoDeTecnica`, antes da busca pela zona.
			bulma.AlvoId = vegeta.Id;
			ServerPlayer? marcado = AlvoDeTecnica(bulma, 5 * T, o => o.Ficha.KO && !o.Ficha.dead);
			bulma.AlvoId = 0;
			AfirmarTf("O CORPO DE VERDADE DO VEGETA SOME: escondido do snapshot, fora da grade, intocavel -- e quem procura um nocauteado na frente, ou o tem marcado na mira, nao o acha",
				vegeta.PossuidoPor == baby.Id && EstaOculto(vegeta.Id) && !EntraNaGrade(vegeta) && vegeta.Combate.Intocavel
				&& naFrente == null && deTecnica == null && marcado == null,
				$"oculto {EstaOculto(vegeta.Id)}, na grade {EntraNaGrade(vegeta)}, intocavel {vegeta.Combate.Intocavel}, "
				+ $"na frente {naFrente?.Name}, de tecnica {deTecnica?.Name}, marcado {marcado?.Name}");
			HospedeAVistaDeTeste = true;
			bool aVista;
			try { aVista = !EstaOculto(vegeta.Id); }
			finally { HospedeAVistaDeTeste = false; }
			AfirmarTf("CONTRA-EXEMPLO (defeito injetado: o corpo do hospede a vista): o mundo ve dois Vegetas -- a regua do esconderijo REPROVA", aVista);

			// (2026-10-25) A TECH DO HOSPEDE RESPONDE AO TUFFLE DENTRO DELE -- o pedido 74 do dono: *"Faca q personagens q viraram neotuffle ou bio,
			// consigam usar os itens tech do personagem anterior antes dele ser substituido"*. Um simulador do Vegeta (a conta dele) aos pes do corpo
			// possuido: o Baby o solta pelo menu E, como o Vegeta faria; a Bulma, no mesmo lugar, ouve que nao e dela.
			var doVegeta = new Obra
			{
				Id = IdDoPalcoDoTuffle + 74, Tipo = Simulador.Tipo, Aparafusada = true,
				X = baby.Pos.X, Y = baby.Pos.Y, DonoConta = vegeta.Conta, DonoNome = vegeta.Name,
			};
			doVegeta.PorZona(baby.Zone);
			_noChao.Add(doVegeta);
			Vec2 ondeABulmaEstava = bulma.Pos;
			try
			{
				List<string> soltouDentro = Ouvir(() => ComandoDeInteracao(baby, "aparafusar", ""));
				bool soltou = !doVegeta.Aparafusada;
				doVegeta.Aparafusada = true;
				bulma.Pos = baby.Pos;
				List<string> daBulmaNaTech = Ouvir(() => ComandoDeInteracao(bulma, "aparafusar", ""));
				bulma.Pos = ondeABulmaEstava;
				List<string> semATech;
				TuffleSemATechDoHospedeDeTeste = true;
				try { semATech = Ouvir(() => ComandoDeInteracao(baby, "aparafusar", "")); }
				finally { TuffleSemATechDoHospedeDeTeste = false; }
				AfirmarTf("**A TECH DO HOSPEDE RESPONDE AO TUFFLE** (o pedido 74): dentro do Vegeta, o Baby SOLTA o simulador do Vegeta pelo menu E, como o "
					+ "Vegeta faria -- e a Bulma, no mesmo lugar, ouve que nao e dela",
					soltou && daBulmaNaTech.Exists(x => x.Contains("nao e seu")) && doVegeta.Aparafusada,
					$"{Ultimos(soltouDentro)} | Bulma: {Ultimos(daBulmaNaTech)}");
				AfirmarTf("CONTRA-EXEMPLO (defeito injetado: o Tuffle sem a tech do hospede, o jogo de antes): o Baby ouve que o simulador nao e dele -- a regua "
					+ "de cima REPROVA", semATech.Exists(x => x.Contains("nao e seu")) && doVegeta.Aparafusada, Ultimos(semATech));
			}
			finally
			{
				TuffleSemATechDoHospedeDeTeste = false;
				bulma.Pos = ondeABulmaEstava;
				_noChao.Remove(doVegeta);
			}

			CravarPosicao(baby, baby.Pos + new Vec2(3 * T, 0));
			TickDasPosses();
			AfirmarTf("...E ANDA COM O TUFFLE: o Baby da tres passos, e o corpo de verdade do Vegeta vai junto -- e dele que o dono assiste",
				Vec2.Distance(vegeta.Pos, baby.Pos) < 1, $"a {Vec2.Distance(vegeta.Pos, baby.Pos):0.#} px");

			vegeta.Combate.Levantar();   // acordado la dentro: so a posse o segura

			// O SENSE MEDIDO COM O VEGETA ACORDADO: nocauteado, a energia dele pode cair abaixo do piso de 5 e o Sense nao o acharia nem sem a
			// posse -- a regua ficava cega numa rodada em oito (a contraprova por mutacao de 2026-10-19 mediu). Acordado ele tem energia de sobra.
			bool sentido = AchoAEnergia(bulma, vegeta);
			HospedeAVistaDeTeste = true;
			bool sentidoNoDefeito;
			try { sentidoNoDefeito = AchoAEnergia(bulma, vegeta); }
			finally { HospedeAVistaDeTeste = false; }
			AfirmarTf("O SENSE NAO ACHA O CORPO ESCONDIDO: de pe e sem posse a Bulma o sentia (o controle), e acordado dentro da posse nao o sente mais",
				sentiaOVegeta && !sentido, $"antes {sentiaOVegeta}, na posse {sentido}");
			AfirmarTf("CONTRA-EXEMPLO (defeito injetado: o corpo do hospede a vista): a Bulma sente o Vegeta escondido -- a regua do Sense REPROVA",
				sentidoNoDefeito);

			bool mexe = PodeMexerOCorpo(vegeta);
			NetDataWriter guarda = Protocol.Begin(Protocol.C2S.Guard);
			guarda.Put(true);
			c[1].Mandar(guarda);
			mesa.Bombeia(() => false, 150);
			bool guardou = vegeta.Combate.Bloqueando;
			AfirmarTf("O DONO SO ASSISTE: mesmo acordado, o Vegeta nao anda (o funil do vetor) e a guarda que ele manda pelo fio e engolida",
				!mexe && !guardou, $"anda {mexe}, guarda {guardou}");
			HospedeComAsRedeasDeTeste = true;
			try
			{
				NetDataWriter deNovo = Protocol.Begin(Protocol.C2S.Guard);
				deNovo.Put(true);
				c[1].Mandar(deNovo);
				mesa.Bombeia(() => vegeta.Combate.Bloqueando, 1000);
			}
			finally { HospedeComAsRedeasDeTeste = false; }
			AfirmarTf("CONTRA-EXEMPLO (defeito injetado: o dono com as redeas): a mesma guarda pelo fio sobe no corpo escondido -- a regua das redeas REPROVA",
				vegeta.Combate.Bloqueando);
			vegeta.Combate.Guardar(false);
			vegeta.Combate.Nocautear(600);

			Persistir(baby);
			double bpGravado = mesa.NoDisco(ContasDaBancadaDoTuffle[0])?.Ficha.BP ?? -1;
			AfirmarTf("O TUFFLE NAO VAI PRO DISCO DENTRO DO OUTRO: o save pedido no meio da posse nao grava o BP emprestado (5.000.000) na conta do Baby",
				bpGravado == bpDoDisco && bpGravado != vegeta.Ficha.BP, $"disco {bpGravado:N0} (antes {bpDoDisco:N0})");
			AfirmarTf("...e ninguem possui duas vezes: dentro do Vegeta o verb do Baby apaga, e o aperto cru diz que ele ja esta noutro corpo",
				!VeOVerb(baby, "hab:tuffle_possuir") && Ouvir(() => UsarHabilidade(baby, "tuffle_possuir")).Any(x => x.Contains("já está dentro")));

			// ============================================================ F) O FIM
			GD.Print("[tuffle] -- F) o fim: o corpo possuido cai e o Tuffle e expulso --");
			if (baby.Combate.Corpo.Achar("Perna esquerda") is { } perna) { perna.Decepado = true; perna.Vida = 0; baby.Combate.SincronizarVida(); }
			Vec2 ondeCaiu = baby.Pos;
			baby.ApontarRumoDoGolpe(new Vec2(T, 0));   // o golpe que derruba vem do leste: e ele que deita o corpo
			baby.Combate.Nocautear(30);
			Facing caiuPara = baby.DirecaoDeitado;
			PosseNaoAcabaNoNocauteDeTeste = true;
			try { TickDasPosses(); }
			finally { PosseNaoAcabaNoNocauteDeTeste = false; }
			AfirmarTf("CONTRA-EXEMPLO (defeito injetado: a posse que nao acaba no nocaute): o corpo cai e o Tuffle continua dentro -- a regua do fim REPROVA",
				PosseDoTuffleDe(baby.Id) != null);
			List<string> expulso = Ouvir(TickDasPosses);
			AfirmarTf("O CORPO POSSUIDO CAI E O TUFFLE E EXPULSO: de volta a PROPRIA ficha (Neo Tuffle, BP dele), de pe, sem a cara do Vegeta, um tile ao lado",
				PosseDoTuffleDe(baby.Id) == null && ReferenceEquals(baby.Ficha, fichaDoBaby) && baby.Ficha.NeoTuffle && !baby.Ficha.KO
				&& baby.CorpoDaPosse == null && Vec2.Distance(baby.Pos, ondeCaiu) is > 1 and <= 2 * T && expulso.Any(x => x.Contains("você é expulso")),
				$"BP {baby.Ficha.BP:N0}, a {Vec2.Distance(baby.Pos, ondeCaiu):0.#} px de onde caiu | {Ultimos(expulso)}");
			AfirmarTf("...E O VEGETA VOLTA A SER CORPO onde o emprestado caiu: a vista, nocauteado, deitado pro mesmo rumo, e com a perna que o emprestado perdeu -- e ele ouve que o Tuffle saiu",
				vegeta.PossuidoPor == 0 && !EstaOculto(vegeta.Id) && !vegeta.Combate.Intocavel && vegeta.Ficha.KO && Vec2.Distance(vegeta.Pos, ondeCaiu) < 1
				&& vegeta.DirecaoDeitado == caiuPara && vegeta.Combate.Corpo.Achar("Perna esquerda")?.Decepado == true
				&& expulso.Any(x => x.Contains("o Neo Tuffle saiu do seu corpo")),
				$"KO {vegeta.Ficha.KO}, oculto {EstaOculto(vegeta.Id)}, deitado pra {vegeta.DirecaoDeitado} (o emprestado: {caiuPara}), "
				+ $"perna {vegeta.Combate.Corpo.Achar("Perna esquerda")?.Decepado}");
			var deVolta = Look(baby);
			AfirmarTf("...e o mundo volta a ver o Baby: o nome dele, a raca dele, sem o cabelo branco -- e de novo no corpo do Neo Tuffle",
				deVolta is { Nome: "Baby", Raca: "Tsujin", CabeloBranco: false, CorpoDoBaby: true, Sobra: 0 }, $"{deVolta}");
			// (2026-10-28, o pedido 144) ...e o Vegeta, no pacote dele: *"ao sair do corpo n faz o cabelo do personagem invadido voltar a cor original"*
			var pacoteDoVegeta = Look(vegeta);
			AfirmarTf("...E O VEGETA COM O CABELO DE SEMPRE: o pacote dele sai com o nome e a raca dele e sem o branco do Tuffle",
				pacoteDoVegeta is { Nome: "Vegeta", Raca: "Saiyan", CabeloBranco: false, CorpoDoBaby: false, Sobra: 0 }, $"{pacoteDoVegeta}");

			// ============================================================ V) SAIR QUANDO QUISER (2026-10-28, o pedido 144)
			GD.Print("[tuffle] -- V) o Tuffle sai do corpo quando quer, e devolve o corpo inteiro --");
			LadoALado(baby, vegeta);
			Vec2 ondeOVegetaEsta = vegeta.Pos;
			ServerPlayer? possuidoDeNovo = Possuir(baby, out _);
			bool dentroComOBotao = possuidoDeNovo == vegeta && VeOVerb(baby, "hab:tuffle_sair");
			List<string> saiuPeloBotao = Ouvir(() => UsarHabilidade(baby, "tuffle_sair"));
			AfirmarTf("O BOTAO \"SAIR DO CORPO\" (o pedido 144: *\"permita o neotuffle ter uma opçao de sair do corpo de quem ele invadiu quando quiser\"*): "
				+ "dentro do Vegeta, de pe, o aperto devolve o Baby ao proprio corpo -- a ficha, a raca, as tecnicas e as formas dele -- e o Vegeta fica "
				+ "caido onde estava; o botao some",
				dentroComOBotao && PosseDoTuffleDe(baby.Id) == null && ReferenceEquals(baby.Ficha, fichaDoBaby) && baby.Race == "Tsujin"
				&& ReferenceEquals(baby.Livro, livroDoBaby) && ReferenceEquals(baby.Forma, formaDoBaby) && baby.CorpoDaPosse == null
				&& vegeta.PossuidoPor == 0 && !EstaOculto(vegeta.Id) && vegeta.Ficha.KO && Vec2.Distance(vegeta.Pos, ondeOVegetaEsta) < 1
				&& saiuPeloBotao.Any(x => x.Contains("você sai do corpo de Vegeta")) && !VeOVerb(baby, "hab:tuffle_sair"),
				$"dentro com o botao {dentroComOBotao}; raca {baby.Race}; ficha propria {ReferenceEquals(baby.Ficha, fichaDoBaby)}; livro proprio "
				+ $"{ReferenceEquals(baby.Livro, livroDoBaby)}; Vegeta caido {vegeta.Ficha.KO} a {Vec2.Distance(vegeta.Pos, ondeOVegetaEsta):0.#} px | {Ultimos(saiuPeloBotao)}");
			AfirmarTf("...e la fora o aperto cru so diz que ele nao esta dentro de corpo nenhum",
				Ouvir(() => UsarHabilidade(baby, "tuffle_sair")).Any(x => x.Contains("você não está dentro de outro corpo")));

			// O CONTRA-EXEMPLO: a mente do Tuffle dentro do corpo (o jogo de antes do pedido 144)
			TuffleComAMenteDeTeste = true;
			try
			{
				LadoALado(baby, vegeta);
				Possuir(baby, out _);
				AfirmarAAscensao(baby);
				AfirmarTf("CONTRA-EXEMPLO (defeito injetado: a mente do Tuffle, o jogo de antes): dentro do Vegeta o Baby segue Tsufurujin, com as tecnicas "
					+ "dele, e a Ascensao do Tsufurujin sobe no poder do Vegeta -- as reguas do corpo inteiro REPROVAM",
					baby.Race == "Tsujin" && ReferenceEquals(baby.Livro, livroDoBaby) && baby.Ficha.BPBoost > 1, $"raca {baby.Race}; bonus {baby.Ficha.BPBoost:0.###}x");
				UsarHabilidade(baby, "tuffle_sair");
			}
			finally { TuffleComAMenteDeTeste = false; }

			// ============================================================ S) A SAIDA DO JOGO NO MEIO
			GD.Print("[tuffle] -- S) a saida do jogo no meio da posse --");
			LadoALado(baby, vegeta);
			ServerPlayer? deNovoNoVegeta = Possuir(baby, out _);
			bool saiu = deNovoNoVegeta != null && mesa.SaiDoJogo(c[0], ContasDaBancadaDoTuffle[0]);
			var noDisco = mesa.NoDisco(ContasDaBancadaDoTuffle[0]);
			AfirmarTf("O TUFFLE SAI DO JOGO DENTRO DO VEGETA: e expulso ANTES do save -- o disco guarda o Neo Tuffle com o BP dele -- e o Vegeta volta caido",
				saiu && noDisco?.Ficha.NeoTuffle == true && noDisco.Ficha.BP == fichaDoBaby.BP && vegeta.PossuidoPor == 0 && !EstaOculto(vegeta.Id)
				&& vegeta.Ficha.KO && _posses.Count == 0,
				$"saiu {saiu}, disco: Neo Tuffle {noDisco?.Ficha.NeoTuffle}, BP {noDisco?.Ficha.BP:N0}");
			ServerPlayer? baby2 = mesa.EntraNoMundo(c[0], ContasDaBancadaDoTuffle[0], "Baby");
			if (baby2 == null) { AfirmarTf("(montagem) o Baby volta ao jogo", false, c[0].Recusa ?? ""); return; }
			LadoALado(baby2, vegeta);
			ServerPlayer? doVolta = Possuir(baby2, out _);
			bool vegetaSaiu = doVolta != null && mesa.SaiDoJogo(c[1], ContasDaBancadaDoTuffle[1]);
			AfirmarTf("O DONO DO CORPO SAI DO JOGO NO MEIO: o Tuffle e expulso pro proprio corpo, e o Vegeta vai pro disco (a posse acaba com ele)",
				vegetaSaiu && baby2.CorpoDaPosse == null && baby2.Ficha.NeoTuffle && _posses.Count == 0
				&& mesa.NoDisco(ContasDaBancadaDoTuffle[1]) != null, $"saiu {vegetaSaiu}, posses {_posses.Count}");

			// ============================================================ D) A MORTE DO CORPO POSSUIDO
			GD.Print("[tuffle] -- D) a morte do corpo possuido e a do Vegeta --");
			ServerPlayer? vegeta2 = mesa.EntraNoMundo(c[1], ContasDaBancadaDoTuffle[1], "Vegeta");
			if (vegeta2 == null) { AfirmarTf("(montagem) o Vegeta volta ao jogo", false, c[1].Recusa ?? ""); return; }
			LadoALado(baby2, vegeta2);
			vegeta2.Combate.Nocautear(600);
			Possuir(baby2, out _);
			bool dentro = PosseDoTuffleDe(baby2.Id)?.Hospede == vegeta2;
			_ = baby2.Combate.Morrer();
			TickDasPosses();
			AfirmarTf("O GOLPE QUE MATA O CORPO POSSUIDO MATA O VEGETA, pelas regras dele (o `Morrer` do corpo dele) -- e o Tuffle sai vivo e de pe",
				dentro && vegeta2.Ficha.dead && !baby2.Ficha.dead && !baby2.Ficha.KO && baby2.CorpoDaPosse == null && _posses.Count == 0,
				$"dentro {dentro}, Vegeta morto {vegeta2.Ficha.dead}, Baby morto {baby2.Ficha.dead}");

			// ============================================================ H) O HOSPEDEIRO PERMANENTE (2026-10-29, o pedido 159)
			GD.Print("[tuffle] -- H) o hospedeiro permanente: o hospede morre, a copia com 10% (o pedido 201), o dominio, a saida e a morte --");
			int CadaveresDe(ServerPlayer onde, string nome) => ZoneList(onde.Zone.Hash).Count(o => o.ECadaver && o.NomeDeQuemMorreu == nome);
			double bpDaBulma = bulma.Ficha.BP;
			LadoALado(baby2, bulma);
			bulma.Combate.Nocautear(600);
			List<string> tomou = Ouvir(() => UsarHabilidade(baby2, "tuffle_hospedar"));
			PosseDoTuffle? perm = PosseDoTuffleDe(baby2.Id);
			var daCopia = Look(baby2);
			string nomeDaCopia = perm?.Corpo.Nome ?? "?";
			AfirmarTf("(o pedido 159) **O HOSPEDEIRO PERMANENTE**: o Tuffle toma a Bulma nocauteada -- ela MORRE (pela porta, pro Outro Mundo), e ele "
					  + "anda numa copia dela com 10% do BP (o pedido 201; era 90%) e o BP do roubo guardado, com o nome dela e o cabelo branco-acinzentado",
				// os 10% do DONO, e nao a constante: a contraprova que volta aos 90% muda a constante, e a regua que lesse dela nao reprovava (a r2)
				perm is { Permanente: true } && bulma.Ficha.dead && Math.Abs(baby2.Ficha.BP - bpDaBulma * 0.10) <= Math.Max(bpDaBulma, 1) * 1e-6
				&& baby2.Ficha.tuffle_bp_roubado == bpDaBulma && baby2.Ficha.tuffle_dominio == 0
				&& daCopia.Nome == nomeDaCopia && daCopia.CabeloBranco,
				$"permanente {perm?.Permanente}; Bulma morta {bulma.Ficha.dead}; BP {baby2.Ficha.BP:N0} de {bpDaBulma:N0}; {daCopia.Nome} branco {daCopia.CabeloBranco} | {Ultimos(tomou)}");
			List<string> outro = Ouvir(() => UsarHabilidade(baby2, "tuffle_hospedar"));
			AfirmarTf("(o pedido 159) ...e DENTRO dela nao se toma outro (*\"ele tem q estar sem hospedeiros pra isso\"*): o verb some da tabela, e o aperto "
					  + "nao muda nada",
				!VeOVerb(baby2, "hab:tuffle_hospedar") && VeOVerb(baby2, "hab:tuffle_sair") && PosseDoTuffleDe(baby2.Id) == perm, Ultimos(outro));
			// ============================================================ (2026-10-31, o pedido 201) O DOMINIO DO CORPO ROUBADO
			double roubado = baby2.Ficha.tuffle_bp_roubado;
			int tiquesNumMinuto = (int)Math.Round(60 / Protocol.TickSeconds);
			double esperadoNumMinuto = Jandirus.Core.Forms.Catalogo.MaestriaPorSegundo * tiquesNumMinuto * Protocol.TickSeconds;
			baby2.Combate.EmCombate = 0;
			baby2.Combate.BrigandoComAlguem = 0;
			double dominio0 = baby2.Ficha.tuffle_dominio;
			for (int i = 0; i < tiquesNumMinuto; i++) TickDasPosses();
			double subiuFora = baby2.Ficha.tuffle_dominio - dominio0;
			double alvoDaConta = BpDoHospedeiro(baby2.Ficha);
			AfirmarTf("(o pedido 201) T2. **O DOMINIO DO CORPO SOBE COM O TEMPO DENTRO DELE, E O PODER VEM JUNTO**: um minuto na copia (fora de luta), no "
					  + "ritmo da maestria das formas (100% em 3 h), e o BP sai dos 10% do roubado pela conta",
				Math.Abs(subiuFora - esperadoNumMinuto) < esperadoNumMinuto * 0.01 && baby2.Ficha.BP > roubado * FracaoInicialDoHospedeiro
				&& Math.Abs(baby2.Ficha.BP - alvoDaConta) <= alvoDaConta * 0.0011,
				$"dominio {dominio0:0.####} -> {baby2.Ficha.tuffle_dominio:0.####} (esperado +{esperadoNumMinuto:0.####}); BP {baby2.Ficha.BP:N0} (conta {alvoDaConta:N0}) de {roubado:N0}");
			double dominio1 = baby2.Ficha.tuffle_dominio;
			for (int i = 0; i < tiquesNumMinuto; i++) { baby2.Combate.EntrarEmCombate(); TickDasPosses(); }
			double subiuLutando = baby2.Ficha.tuffle_dominio - dominio1;
			baby2.Combate.EmCombate = 0;
			baby2.Combate.BrigandoComAlguem = 0;
			baby2.Combate.LutandoDeVerdade = 0;
			AfirmarTf("(o pedido 201) T2b. **...E LUTANDO O DOMINIO SOBE NO DOBRO**", Math.Abs(subiuLutando - 2 * esperadoNumMinuto) < esperadoNumMinuto * 0.02,
				$"+{subiuLutando:0.####} num minuto lutando, +{subiuFora:0.####} fora");
			// (2026-10-31, o adendo 201d, a escolha do dono: "Linha reta até 110%") a LINHA RETA de 10% a 110%, nos numeros do dono e nao
			// nas constantes: com 50% de dominio o corpo rende 60% do que tinha; com 90%, o que tinha; com 99%, 109%
			bool NaLinhaReta(double dominio)
			{
				baby2.Ficha.tuffle_dominio = dominio;
				TickDasPosses();
				double conta = roubado * (0.10 + 1.00 * baby2.Ficha.tuffle_dominio / 100);
				return Math.Abs(baby2.Ficha.BP - conta) <= conta * 0.0011;
			}
			bool reta50 = NaLinhaReta(50);
			double bp50 = baby2.Ficha.BP;
			bool reta90 = NaLinhaReta(90);
			double bp90 = baby2.Ficha.BP;
			bool reta99 = NaLinhaReta(99);
			double bp99 = baby2.Ficha.BP;
			// (o adendo 201d) a reta chega ao fim em passos pequenos: o ultimo (de 99,95% pra 100%) fica abaixo dos 0,1% que o BP espera pra
			// subir, e o corpo parava um fio abaixo dos 110% (o robo `--diagtuffle` viu 54.999.654 de 55.000.000) -- o T3 confere dai
			NaLinhaReta(99.95);
			baby2.Ficha.tuffle_dominio = 99.9999;
			List<string> inteiro = Ouvir(() => TickDasPosses());
			// os 110% do DONO (o adendo 201d), e nao a constante: a contraprova que volta aos 100% muda a constante
			bool noTeto = baby2.Ficha.tuffle_dominio == 100 && Math.Abs(baby2.Ficha.BP - roubado * 1.10) <= roubado * 1e-9;
			baby2.Ficha.BP = roubado * 3;   // um ganho que escapasse por outra porta (o Zenkai, um desejo)
			TickDasPosses();
			AfirmarTf("(o adendo 201d) T3. **DOMINADO POR INTEIRO, O CORPO RENDE 110% DO PODER QUE TINHA -- E NUNCA PASSA DISSO**: com 100% de "
					  + "dominio o BP e 110% do roubo (e o Tuffle e avisado), e um ganho por outra porta volta pro teto no tique seguinte",
				noTeto && inteiro.Any(t => t.Contains("por inteiro: ele rende 110%", StringComparison.Ordinal))
				&& Math.Abs(baby2.Ficha.BP - roubado * 1.10) <= roubado * 1e-9,
				$"no teto {noTeto}; BP {baby2.Ficha.BP:N0} de {roubado:N0} (110% = {roubado * 1.10:N0}) | {Ultimos(inteiro)}");
			AfirmarTf("(o adendo 201d) T3b. **O DOMINIO SOBE O PODER EM LINHA RETA ATE OS 110%** (a escolha do dono): com 50% de dominio o "
					  + "corpo rende 60% do que tinha, com 90% o que tinha, e com 99% ja passa dele (109%) -- sem salto no fim",
				reta50 && reta90 && reta99 && bp99 > roubado,
				$"com 50%: {bp50:N0}; com 90%: {bp90:N0}; com 99%: {bp99:N0} (109% = {roubado * 1.09:N0}) -- de {roubado:N0}");
			double ganhoNaCopia = baby2.Ficha.GanhoDeAtividade(10);
			AfirmarTf("(o pedido 201) T4. **NEM O CORPO ROUBADO TREINA** (*\"vc n consegue treinar nem seu corpo original nem corpos invadidos\"*): o "
					  + "ganho de uma atividade na copia e zero", ganhoNaCopia == 0, $"ganho na copia {ganhoNaCopia}");
			int antesDaSaida = CadaveresDe(baby2, nomeDaCopia);
			List<string> saiu2 = Ouvir(() => UsarHabilidade(baby2, "tuffle_sair"));
			int depoisDaSaida = CadaveresDe(baby2, nomeDaCopia);
			double ganhoNoProprio = baby2.Ficha.GanhoDeAtividade(10);
			AfirmarTf("(o pedido 159) **SAIR DELA A DEIXA MORTA NO CHAO** (um cadaver novo com o nome dela), e o Tuffle volta vivo pro proprio corpo -- "
					  + "onde NAO ganha BP (*\"Neotuffles n poderiam ganhar bp fora do host\"*; e desde o pedido 201 nem na copia -- o T4)",
				PosseDoTuffleDe(baby2.Id) == null && baby2.CorpoDaPosse == null && baby2.Ficha.NeoTuffle && !baby2.Ficha.dead
				&& depoisDaSaida == antesDaSaida + 1 && ganhoNoProprio == 0,
				$"cadaveres {antesDaSaida} -> {depoisDaSaida}; ganho na copia {ganhoNaCopia}, no proprio {ganhoNoProprio} | {Ultimos(saiu2)}");

			// ============================================================ L) O LOGOUT DENTRO DO HOSPEDEIRO (2026-10-30, o pedido 180)
			// O dono: *"guarda a posse"* -- sair do jogo dentro da copia nao a deixa mais morta no chao: o disco leva o corpo dele e a copia, e o
			// login o poe de volta dentro dela. E os buffs sao do corpo que os acendeu: a entrada apaga os dele, a saida apaga os da copia.
			GD.Print("[tuffle] -- L) a saida do jogo dentro do hospedeiro permanente: a posse fica (o pedido 180) --");
			bool Perto(double x, double y) => Math.Abs(x - y) < 1;   // o buff da bancada e de +1000 de physoff
			ServerPlayer? chichi = Entrar(4, Ficha("Chichi", "Human", "Female"));
			if (chichi == null) { AfirmarTf("(montagem) a Chichi entra pela porta", false, c[4].Recusa ?? ""); return; }
			LadoALado(baby2, chichi);
			chichi.Combate.Nocautear(600);
			chichi.Livro.Dar(PathDoZanzoken);   // uma tecnica dela, que a copia leva e o disco tem de guardar
			double physoffDoBaby = baby2.Ficha.physoff;
			LigarBuff(baby2, "bancada_tuffle", "Bancada", new() { ["physoff"] = 1000 });
			bool buffNele = baby2.Ficha.physoff > physoffDoBaby + 500;
			UsarHabilidade(baby2, "tuffle_hospedar");
			PosseDoTuffle? naChichi = PosseDoTuffleDe(baby2.Id);
			AfirmarTf("(o pedido 180) **A ENTRADA APAGA OS BUFFS DELE**: com um buff de pe, o Tuffle toma a Chichi -- o buff sai da ficha DELE (o "
					  + "numero volta) e nao fica nada aceso pra copia",
				buffNele && naChichi is { Permanente: true } && Perto(naChichi.FichaDoTuffle.physoff, physoffDoBaby) && BuffsDe(baby2.Id).Count == 0,
				$"buff nele {buffNele}; physoff guardado {naChichi?.FichaDoTuffle.physoff:0.##} (era {physoffDoBaby:0.##}); buffs de pe {BuffsDe(baby2.Id).Count}");
			if (naChichi is not { Permanente: true }) return;
			string nomeDaChichi = naChichi.Corpo.Nome;
			double bpDaCopia = baby2.Ficha.BP, bpDoProprio = naChichi.FichaDoTuffle.BP, physoffDaCopia = baby2.Ficha.physoff;
			LigarBuff(baby2, "bancada_tuffle", "Bancada", new() { ["physoff"] = 1000 });   // e um aceso NA copia, que o save tira dela
			// ...e a copia sem a perna esquerda, com as tecnicas dela: os dois voltam do disco como estavam
			if (baby2.Combate.Corpo.Achar("Perna esquerda") is { } pernaDaCopia) { pernaDaCopia.Decepado = true; pernaDaCopia.Vida = 0; }
			int tecnicasDaCopia = baby2.Livro.Aprendidas.Count;
			ZoneKey zonaDaCena = baby2.Zone;
			int CadaveresDaChichi() => ZoneList(zonaDaCena.Hash).Count(o => o.ECadaver && o.NomeDeQuemMorreu == nomeDaChichi);
			int antesDoLogout = CadaveresDaChichi(), idDeAntes = baby2.Id;
			// (2026-10-31, o pedido 201) A COPIA DE ANTES DA REGRA: sem o BP do roubo gravado (como as que a VPS tem, do pedido 159) -- o login
			// tem de migra-la sem tirar poder (o T5, la embaixo).
			baby2.Ficha.tuffle_bp_roubado = 0;
			baby2.Ficha.tuffle_dominio = 0;
			bool saiuDentro = mesa.SaiDoJogo(c[0], ContasDaBancadaDoTuffle[0]);
			CharacterSave? disco = mesa.NoDisco(ContasDaBancadaDoTuffle[0]);
			AfirmarTf("(o pedido 180) **O DISCO GUARDA OS DOIS CORPOS**: o do Tuffle (Neo Tuffle, Tsufurujin, sem o buff) e a copia da Chichi por cima "
					  + "(Humana, sem o buff aceso nela) -- e a posse sai do mapa com o id que saiu do jogo",
				saiuDentro && disco is { Raca: "Tsujin", Hospedeiro: { Raca: "Human" } h } && disco.Ficha.NeoTuffle && Perto(disco.Ficha.physoff, physoffDoBaby)
				&& Perto(h.Ficha.physoff, physoffDaCopia) && h.Corpo.Nome == nomeDaChichi && !_posses.ContainsKey(idDeAntes),
				$"saiu {saiuDentro}; disco {disco?.Raca}, Neo Tuffle {disco?.Ficha.NeoTuffle}, physoff {disco?.Ficha.physoff:0.##}; copia "
				+ $"{disco?.Hospedeiro?.Raca} '{disco?.Hospedeiro?.Corpo.Nome}', physoff {disco?.Hospedeiro?.Ficha.physoff:0.##} (era {physoffDaCopia:0.##}); "
				+ $"posse no id antigo {_posses.ContainsKey(idDeAntes)}");
			ServerPlayer? baby3 = mesa.EntraNoMundo(c[0], ContasDaBancadaDoTuffle[0], "Baby");
			if (baby3 == null) { AfirmarTf("(montagem) o Baby volta ao jogo", false, c[0].Recusa ?? ""); return; }
			var deVoltaNaCopia = Look(baby3);
			AfirmarTf("(o pedido 201) T5. **A COPIA GRAVADA ANTES DA REGRA NOVA NAO PERDE PODER NO LOGIN**: o BP do roubo vira o dela sobre 0,9, e o "
					  + "dominio, o que rende os mesmos 90% (80%, na linha reta ate 110% do adendo 201d)",
				Math.Abs(baby3.Ficha.tuffle_bp_roubado - bpDaCopia / 0.9) <= Math.Max(bpDaCopia, 1) * 1e-6
				&& Math.Abs(FracaoDoHospedeiro(baby3.Ficha.tuffle_dominio) - 0.9) < 1e-9 && Math.Abs(baby3.Ficha.BP - bpDaCopia) <= Math.Max(bpDaCopia, 1) * 1e-6,
				$"roubado {baby3.Ficha.tuffle_bp_roubado:N0} (esperado {bpDaCopia / 0.9:N0}); dominio {baby3.Ficha.tuffle_dominio:0.###}; BP {baby3.Ficha.BP:N0}");
			bool semAPerna = baby3.Combate.Corpo.Achar("Perna esquerda")?.Decepado == true;
			AfirmarTf("(o pedido 180) **A POSSE FICA NO LOGOUT** (*\"guarda a posse\"*): o Tuffle volta ao jogo DENTRO da copia da Chichi -- o nome e "
					  + "o cabelo branco dela, a raca dela, o BP dela, a perna que ela perdeu, as tecnicas dela, os ganchos do corpo -- e o logout "
					  + "nao deixou cadaver nenhum no chao",
				PosseDoTuffleDe(baby3.Id) is { Permanente: true } && baby3.CorpoDaPosse != null && deVoltaNaCopia.Nome == nomeDaChichi
				&& deVoltaNaCopia.CabeloBranco && baby3.Race == "Human" && Math.Abs(baby3.Ficha.BP - bpDaCopia) <= Math.Max(bpDaCopia, 1) * 1e-6
				&& semAPerna && baby3.Livro.Sabe(PathDoZanzoken) && baby3.Livro.Aprendidas.Count == tecnicasDaCopia
				&& baby3.Combate.AoDecepar != null && baby3.Combate.RitmoDaRecarga != null && CadaveresDaChichi() == antesDoLogout,
				$"posse {PosseDoTuffleDe(baby3.Id)?.Permanente}; {deVoltaNaCopia.Nome} branco {deVoltaNaCopia.CabeloBranco}; raca {baby3.Race}; BP "
				+ $"{baby3.Ficha.BP:N0} de {bpDaCopia:N0}; sem a perna {semAPerna}; tecnicas {baby3.Livro.Aprendidas.Count} de {tecnicasDaCopia}; "
				+ $"ganchos {baby3.Combate.AoDecepar != null}; cadaveres {antesDoLogout} -> {CadaveresDaChichi()}");
			LigarBuff(baby3, "bancada_tuffle", "Bancada", new() { ["physoff"] = 1000 });
			List<string> saiu3 = Ouvir(() => UsarHabilidade(baby3, "tuffle_sair"));
			AfirmarTf("(o pedido 180) **A SAIDA DEPOIS DO RELOG DEVOLVE O CORPO DELE INTEIRO**: o Neo Tuffle Tsufurujin, com o BP e o physoff dele "
					  + "(o buff aceso na copia apagou NELA), e a copia cai morta",
				PosseDoTuffleDe(baby3.Id) == null && baby3.CorpoDaPosse == null && baby3.Ficha.NeoTuffle && baby3.Race == "Tsujin"
				&& Math.Abs(baby3.Ficha.BP - bpDoProprio) <= Math.Max(bpDoProprio, 1) * 1e-6 && Perto(baby3.Ficha.physoff, physoffDoBaby)
				&& BuffsDe(baby3.Id).Count == 0 && CadaveresDaChichi() == antesDoLogout + 1,
				$"raca {baby3.Race}; BP {baby3.Ficha.BP:N0} de {bpDoProprio:N0}; physoff {baby3.Ficha.physoff:0.##} (era {physoffDoBaby:0.##}); "
				+ $"buffs de pe {BuffsDe(baby3.Id).Count}; cadaveres {CadaveresDaChichi()} | {Ultimos(saiu3)}");
			baby2 = baby3;   // a familia de baixo segue com o Baby que voltou

			ServerPlayer? goku = Entrar(3, Ficha("Goku", "Saiyan", "Male"));
			if (goku == null) { AfirmarTf("(montagem) o Goku entra pela porta", false, c[3].Recusa ?? ""); return; }
			LadoALado(baby2, goku);
			goku.Combate.Nocautear(600);
			UsarHabilidade(baby2, "tuffle_hospedar");
			string nomeDoGoku = PosseDoTuffleDe(baby2.Id)?.Corpo.Nome ?? "?";
			bool noGoku = PosseDoTuffleDe(baby2.Id) is { Permanente: true } && goku.Ficha.dead;
			int antesDaMorte = CadaveresDe(baby2, nomeDoGoku);
			_ = baby2.Combate.Morrer();   // o golpe que mata a copia (o corpo emprestado nao tem a porta da morte: quem a cobra e o tique)
			TickDasPosses();
			AfirmarTf("(o pedido 159) **A MORTE DA COPIA LEVA O TUFFLE JUNTO** (*\"Se o corpo do neotuffle hospedeiro morrer o neotuffle morre junto\"*): a copia "
					  + "fica no chao, e o Tuffle morre no proprio corpo",
				noGoku && PosseDoTuffleDe(baby2.Id) == null && baby2.CorpoDaPosse == null && baby2.Ficha.NeoTuffle && baby2.Ficha.dead
				&& CadaveresDe(baby2, nomeDoGoku) == antesDaMorte + 1,
				$"no Goku {noGoku}; Baby morto {baby2.Ficha.dead}; cadaveres {antesDaMorte} -> {CadaveresDe(baby2, nomeDoGoku)}");
		}
		catch (Exception e)
		{
			AfirmarTf("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			PosseSemAMaquinaDeTeste = HospedeAVistaDeTeste = HospedeComAsRedeasDeTeste = TuffleSemCabeloBrancoDeTeste = PosseNaoAcabaNoNocauteDeTeste = false;
			TuffleSemCorpoDoBabyDeTeste = TuffleComAMenteDeTeste = false;
			Jandirus.Core.Tech.ArteDoLaboratorio.LabSemCellPodDeTeste = false;
			foreach (PosseDoTuffle p in _posses.Values.Distinct().ToList())
				if (p.Permanente) DeixarOHospedeiro(p, "", morreJunto: false);
				else Expulsar(p, "");
			_noChao.RemoveAll(o => o.Id >= IdDoPalcoDoTuffle && o.Id < IdDoPalcoDoTuffle + 100);
			mesa.Fecha();
			ApagarContasDoTuffle();
			GD.Print($"[tuffle] ================ {_tfOk} OK, {_tfFalhou} FALHA(S) ================");
		}
	}
}
