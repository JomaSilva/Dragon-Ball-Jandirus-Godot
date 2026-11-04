using Godot;
using Jandirus.Core.Magic;
using Jandirus.Net;
using Tarefa = System.Threading.Tasks.Task;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA CHAVE DA SALA DO TEMPO (`--diagchavesala`, 2026-10-31, os pedidos 196 e 197) -- as fotos de cada exemplo do dono:
///   C1. o VILAO DA TERRA cai e deixa a chave (o sorteio que cai, na mao do palco): o chat diz, e ela vai direto pra mochila -- foto
///       `chave-1-o-vilao-da-terra-deixa-a-chave`; a MOCHILA aberta com ela, na arte padrao de chave -- foto `chave-2-a-chave-na-mochila`;
///   C2. as BLACK STAR (as sete ao lado, `db_invocar`): a linha "A Sala do Tempo mais próxima", pedida pela janela -- e o dragao diz o mundo,
///       o setor e o tempo la dentro -- foto `chave-3-black-star-a-sala-mais-proxima`;
///   C3. o SUPER SHENLONG (as sete na mao, de qualquer lugar so na bancada): a linha "Revelar uma Sala do Tempo" PERGUNTA qual -- foto
///       `chave-4-super-qual-sala` --, e a resposta "2 anos por dia" revela a sala -- foto `chave-5-super-revela-a-sala`;
///   C4. o SHENRON (o palco da janela de desejos): a linha "Chave da Sala do Tempo" na lista -- foto `chave-6-o-desejo-da-chave-na-lista` --,
///       pedida: a chave vai pra mochila -- foto `chave-7-o-dragao-da-a-chave`;
///   C5. a PORTA da Sala da Terra, sem a autorizacao do Guardiao: com a chave, `sala_entrar` atravessa e a chave quebra -- foto
///       `chave-8-a-chave-abre-a-sala-e-quebra`;
///   C6. (2026-11-01, o pedido 222) o PISO DOS 20 MILHOES de poder base: com 19 milhoes, a porta recusa e diz o piso, sem gastar a chave -- foto
///       `chave-9-o-piso-dos-20-milhoes-recusa`; com os 20 milhoes, a mesma porta abre (e a chave quebra) -- foto `chave-10-com-20-milhoes-entra`.
///
///     Godot --path . --host --rede 8486 --campoteste 8 --horateste 0.5 --semfauna --diagchavesala --raca Human --conta bancada_chavesala --nome Chaveiro
/// </summary>
public partial class RoboDaChaveDaSala : Node
{
	private readonly List<string> _linhas = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _falas = [];

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

	private void AoFalar(Protocol.Fala canal, string autor, string texto) => _falas.Add(texto);

	private bool Ouviu(string trecho) => _falas.Exists(f => f.Contains(trecho, StringComparison.Ordinal));

	private string Ultimas() => string.Join(" | ", _falas.TakeLast(4));

	private async Tarefa Percurso()
	{
		bool pronto = await Ate(() => C is { Connected: true, LocalId: > 0 } && World.Instancia is { PosicaoLocalDeTeste: not null } && S != null
									  && MenuDeDesejos.Instancia != null && TelaDeInventario.Instancia != null, 60);
		Conferir(pronto, "em 60 s o cliente conectou, o mundo, a janela de desejos e a mochila nasceram, e o servidor esta neste processo");
		if (!pronto || C is not { } cli || S is not { } srv || MenuDeDesejos.Instancia is not { } janela || TelaDeInventario.Instancia is not { } mochila) return;
		GD.Print("[dchavesala] ===== A CHAVE DA SALA DO TEMPO E AS SALAS QUE OS DRAGOES REVELAM (dono, 2026-10-31, os pedidos 196 e 197) =====");
		int eu = cli.LocalId;
		cli.Falou += AoFalar;
		srv.EmbateDeFoto_CeuLimpo(eu);
		await Segundos(1.5);

		// ------------------------------------------------------------ C1. o vilao da Terra deixa a chave
		_falas.Clear();
		string vilao = srv.PalcoCS_OVilaoDeixaAChave(eu);
		await Ate(() => Ouviu("deixou cair uma Chave da Sala do Tempo"), 5);
		await Segundos(0.6);
		Conferir(vilao.Length > 0 && srv.PalcoCS_Chaves(eu) == 1 && Ouviu($"o vilão {vilao} deixou cair uma Chave da Sala do Tempo -- e ela foi direto pra sua mochila"),
				 $"C1. **O VILAO DA TERRA DEIXA A CHAVE** (o sorteio que cai): ela vai direto pra mochila, e o chat diz [vilao '{vilao}'; {srv.PalcoCS_Chaves(eu)} chave(s); {Ultimas()}]");
		Fotografar("chave-1-o-vilao-da-terra-deixa-a-chave");

		mochila.Abrir();
		await Quadros(10);
		Conferir(cli.Mochila.Quantos(Jandirus.Core.Items.CatalogoDeItens.ChaveDaSala) > 0,
				 "C1b. ...e a MOCHILA aberta mostra a Chave da Sala do Tempo (a arte padrao de chave)");
		Fotografar("chave-2-a-chave-na-mochila");
		mochila.Fechar();
		await Quadros(4);

		// ------------------------------------------------------------ C2. as Black Star: a sala mais proxima
		int postas = srv.PalcoEN_SeteAoLado(eu);
		await Segundos(1.0);
		_falas.Clear();
		cli.SendVerbo("db_invocar");
		bool abriu = await Ate(() => janela.NaTela && janela.EtapaDeTeste == "Lista", 15);
		await Segundos(2.0);
		bool temALinha = janela.LinhasDesenhadas().Exists(l => l.Id == Desejos.IdDaSalaMaisProxima && l.Pode);
		Conferir(postas == 7 && abriu && temALinha, $"C2. as Black Star sobem, e a janela traz a linha \"A Sala do Tempo mais próxima\" [sete {postas}; janela {abriu}]");
		bool pediu = await Pedir(janela, Desejos.IdDaSalaMaisProxima, null);
		bool disse = await Ate(() => Ouviu("a Sala do Tempo mais perto de você fica em"), 8);
		await Segundos(1.0);
		Conferir(pediu && disse && Ouviu("no setor ["),
				 $"C2b. **AS BLACK STAR DIZEM A SALA DO TEMPO MAIS PROXIMA**: o mundo, o setor da carta estelar e o tempo la dentro [{Ultimas()}]");
		Fotografar("chave-3-black-star-a-sala-mais-proxima");
		await Ate(() => !janela.NaTela, 8);

		// ------------------------------------------------------------ C3. o Super Shenlong: qual sala, e onde
		srv.SuperShenronDeQualquerLugarDeTeste = true;
		try
		{
			srv.SupersNaMaoDeTeste(eu);
			await Segundos(1.0);
			_falas.Clear();
			cli.SendVerbo("sdb_invocar");
			bool abriuOSuper = await Ate(() => janela.NaTela && janela.EtapaDeTeste == "Lista"
											  && janela.LinhasDesenhadas().Exists(l => l.Id == Desejos.IdDaSalaRevelada), 15);
			await Segundos(1.5);
			bool perguntou = abriuOSuper && janela.ApertarLinha(Desejos.IdDaSalaRevelada);
			await Quadros(10);
			List<(string Rotulo, string Nota, bool Pode, bool Marcado)> resp = janela.RespostasDesenhadas();
			Conferir(perguntou && janela.EtapaDeTeste == "Resposta" && resp.Count == 7 && resp[0].Rotulo.StartsWith("3 meses")
					 && resp[^1].Rotulo.StartsWith("5 anos"),
					 $"C3. **O SUPER SHENLONG PERGUNTA QUAL SALA**: \"{janela.PerguntaDesenhada}\" -- de 3 meses a 5 anos [{string.Join(" | ", resp.Select(r => r.Rotulo))}]");
			Fotografar("chave-4-super-qual-sala");
			bool respondeu = janela.ApertarResposta("2 anos por dia");
			await Quadros(6);
			bool pediuAoSuper = janela.ApertarBotao("Pedir");
			bool revelou = await Ate(() => Ouviu("SUPER SHENLONG: a Sala do Tempo de 2 anos mais perto de você fica em"), 8);
			await Segundos(1.0);
			Conferir(respondeu && pediuAoSuper && revelou && Ouviu("no setor ["),
					 $"C3b. **...E REVELA A SALA DE 2 ANOS** mais perto: o mundo e o setor [{Ultimas()}]");
			Fotografar("chave-5-super-revela-a-sala");
		}
		finally { srv.SuperShenronDeQualquerLugarDeTeste = false; }
		await Ate(() => !janela.NaTela, 8);

		// ------------------------------------------------------------ C4. o Shenron: o desejo da chave
		srv.PalcoDosDesejosDeTeste(eu);
		await Segundos(1.0);
		_falas.Clear();
		cli.SendVerbo("db_invocar");
		bool abriuShenron = await Ate(() => janela.NaTela && janela.EtapaDeTeste == "Lista"
										   && janela.LinhasDesenhadas().Exists(l => l.Id == Desejos.IdDaChaveDaSala), 15);
		await Segundos(2.0);
		Conferir(abriuShenron && janela.LinhasDesenhadas().Exists(l => l.Id == Desejos.IdDaChaveDaSala && l.Pode && l.Nome == "Chave da Sala do Tempo"),
				 "C4. **A CHAVE DA SALA DO TEMPO E UM DESEJO DO SHENRON**: a linha esta na lista, acesa");
		Fotografar("chave-6-o-desejo-da-chave-na-lista");
		bool pediuAChave = await Pedir(janela, Desejos.IdDaChaveDaSala, "Para mim");
		bool deu = await Ate(() => srv.PalcoCS_Chaves(eu) == 2, 8);
		await Segundos(0.8);
		janela.ApertarBotao("Fechar");
		await Quadros(6);
		Conferir(pediuAChave && deu && Ouviu("o dragão te dá uma Chave da Sala do Tempo -- e ela foi direto pra sua mochila"),
				 $"C4b. **...E O DRAGAO DA A CHAVE**, direto pra mochila (agora sao duas) [{srv.PalcoCS_Chaves(eu)} chave(s); {Ultimas()}]");
		Fotografar("chave-7-o-dragao-da-a-chave");
		srv.PalcoCS_AdormecerOSetComum(eu);
		srv.DesfazerOPalcoDosDesejosDeTeste();

		// ------------------------------------------------------------ C5. a porta: a chave abre e quebra
		_falas.Clear();
		bool naPorta = srv.PalcoCS_NaPortaDaSala(eu)
					   && await Ate(() => cli.Zone.Name == "Lookout" && World.Instancia?.ZonaMontadaDeTeste == cli.Zone.Hash, 30);
		await Segundos(2.0);
		cli.SendVerbo("sala_entrar");
		bool dentro = await Ate(() => Jandirus.Core.World.SalaDoTempo.EhASala(cli.Zone) && World.Instancia?.ZonaMontadaDeTeste == cli.Zone.Hash, 20);
		await Segundos(2.5);
		Conferir(naPorta && dentro && srv.PalcoCS_Chaves(eu) == 1 && Ouviu("a chave se parte em pedaços"),
				 $"C5. **A CHAVE ABRE A SALA SEM O GUARDIAO, E QUEBRA**: atravessou a porta sem a autorizacao, e uma das duas chaves se partiu [porta {naPorta}; "
				 + $"dentro {dentro}; {srv.PalcoCS_Chaves(eu)} chave(s); {Ultimas()}]");
		Fotografar("chave-8-a-chave-abre-a-sala-e-quebra");
		srv.PalcoCS_DeVolta(eu);
		await Ate(() => !Jandirus.Core.World.SalaDoTempo.EhASala(cli.Zone) && World.Instancia?.ZonaMontadaDeTeste == cli.Zone.Hash, 30);

		// ------------------------------------------------------------ C6. (2026-11-01, o pedido 222) o piso dos 20 milhoes de poder base
		// *"so pode entrar na sala do tempo quem tiver NO MINIMO 20 milhoes de bp base"* -- a mesma porta, a mesma chave que sobrou
		const string FraseDoPiso = "a Sala do Tempo só deixa entrar quem tem no mínimo 20 milhões de poder base";
		_falas.Clear();
		// 19 milhoes, e nao 19.999.999: o treino da espera na porta (a gravidade do Templo) ja cruzaria o piso sozinho
		double bpAntes = srv.PalcoCS_PisoDaSala(eu, true, 19_000_000);
		bool naPorta2 = srv.PalcoCS_NaPortaDaSala(eu)
						&& await Ate(() => cli.Zone.Name == "Lookout" && World.Instancia?.ZonaMontadaDeTeste == cli.Zone.Hash, 30);
		await Segundos(2.0);
		cli.SendVerbo("sala_entrar");
		bool recusou = await Ate(() => Ouviu(FraseDoPiso), 8);
		await Segundos(1.0);
		Conferir(naPorta2 && recusou && !Jandirus.Core.World.SalaDoTempo.EhASala(cli.Zone) && srv.PalcoCS_Chaves(eu) == 1,
				 $"C6. (222) **ABAIXO DOS 20 MILHOES DE PODER BASE (19 MILHOES), A PORTA RECUSA** e diz o piso -- e a chave NAO se gasta [porta {naPorta2}; "
				 + $"recusou {recusou}; zona {cli.Zone.Name}; {srv.PalcoCS_Chaves(eu)} chave(s); {Ultimas()}]");
		Fotografar("chave-9-o-piso-dos-20-milhoes-recusa");

		_falas.Clear();
		srv.PalcoCS_PisoDaSala(eu, true, 20_000_000);
		await Segundos(0.5);
		cli.SendVerbo("sala_entrar");
		bool entrou = await Ate(() => Jandirus.Core.World.SalaDoTempo.EhASala(cli.Zone) && World.Instancia?.ZonaMontadaDeTeste == cli.Zone.Hash, 20);
		await Segundos(2.5);
		Conferir(entrou && !Ouviu(FraseDoPiso) && srv.PalcoCS_Chaves(eu) == 0,
				 $"C6b. ...e com os 20 MILHOES a mesma porta abre (a ultima chave quebra) [dentro {entrou}; {srv.PalcoCS_Chaves(eu)} chave(s); {Ultimas()}]");
		Fotografar("chave-10-com-20-milhoes-entra");
		srv.PalcoCS_PisoDaSala(eu, false, bpAntes);
		srv.PalcoCS_DeVolta(eu);
		await Ate(() => !Jandirus.Core.World.SalaDoTempo.EhASala(cli.Zone) && World.Instancia?.ZonaMontadaDeTeste == cli.Zone.Hash, 30);
	}

	/// <summary>A JANELA PEDINDO: a linha, a resposta (se a linha pergunta) e o "Pedir" do confirmar -- o caminho do dedo.</summary>
	private async System.Threading.Tasks.Task<bool> Pedir(MenuDeDesejos janela, string id, string? resposta)
	{
		if (!janela.ApertarLinha(id)) return false;
		await Quadros(6);
		if (janela.EtapaDeTeste == "Resposta")
		{
			if (resposta == null || !janela.ApertarResposta(resposta)) return false;
			await Quadros(6);
		}
		if (janela.EtapaDeTeste != "Confirmar") return false;
		bool apertou = janela.ApertarBotao("Pedir");
		await Quadros(6);
		return apertou;
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
		if (S is { } srv) srv.SuperShenronDeQualquerLugarDeTeste = false;
		if (C is { } cli) cli.Falou -= AoFalar;
		foreach (string l in _linhas) GD.Print("[dchavesala] " + l);
		GD.Print(_falhas.Count == 0 ? "[dchavesala] ===== TUDO OK =====" : $"[dchavesala] ===== {_falhas.Count} FALHA(S) =====");
		foreach (string f in _falhas) GD.Print("[dchavesala]   - " + f);
		GetTree().Quit(_falhas.Count == 0 ? 0 : 1);
	}
}
