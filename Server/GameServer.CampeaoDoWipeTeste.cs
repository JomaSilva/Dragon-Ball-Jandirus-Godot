using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Stats;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--campeaoteste` -- O CAMPEAO DO MUNDO ANTERIOR (o pedido 94 do dono, 2026-10-26). Ver `GameServer.CampeaoDoWipe.cs`.
///
/// Numa pasta de saves TEMPORARIA (o palco das bancadas: a pasta de verdade nunca e tocada), pelo caminho de producao -- o
/// `ExecutarLimpeza` de verdade, os jogadores entrando pela porta, o tique e o funil da derrota:
///   A. A LIMPEZA GUARDA O MAIS FORTE -- tres contas no disco (o campeao, um segundo lugar e uma de BANCADA mais forte que tem de ser
///      ignorada) e um jogador ONLINE cujo disco diz mais do que ele tem agora: guarda-se quem tem o maior BP base DE AGORA, o save
///      inteiro (skills, formas), e o arquivo dele e o unico que sobra na pasta.
///   B. OS 50% -- um jogador do mundo novo com 49% do BP base do campeao nao o acorda; com 50%, ele nasce NA TERRA: o nome, a raca, o BP
///      base cravado, as skills, a forma mais forte que despertou (o SSJ2, e nao o SSJ), letal, Ki de chefe, cacando o planeta, o premio
///      na bolsa -- e o mundo ouve.
///   C. O REINICIO -- o disco relido diz "na Terra" com o corpo zero, e o primeiro tique o poe de volta, na mesma forma.
///   D. A DERROTA -- o jogador que o derruba (o funil `AoPerderALuta`) leva o premio, o arquivo diz "derrotado", o corpo sai no tique
///      seguinte, e ele nao volta neste wipe nem com o jogador muito acima dos 50%.
///   E. A LIMPEZA SEGUINTE, SEM NINGUEM A ALTURA -- o campeao guardado fica, e volta a esperar.
///   F. O ADMIN -- o verbo o chama na Terra na hora.
///
///     Godot --headless -- --server --port N --campeaoteste
/// </summary>
public sealed partial class GameServer
{
	private int _cwOk, _cwFalhou;
	private bool _campeaoTesteRodou;

	private const string SkillDoCampeaoA = "/datum/skill/mind/Ki_Unlocked";
	private const string SkillDoCampeaoB = "/datum/skill/rank/Kamehameha";

	private void AfirmarCw(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _cwOk++; GD.Print($"[campeao]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _cwFalhou++; GD.PrintErr($"[campeao]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoCampeao(int porta)
	{
		if (_campeaoTesteRodou) return;
		_campeaoTesteRodou = true;
		_cwOk = _cwFalhou = 0;
		GD.Print("[campeao] ================ O CAMPEAO DO MUNDO ANTERIOR (dono, 2026-10-26) ================");

		PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada();
		var online = new ClienteCruDeTeste();
		var heroi = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, online, heroi);
		List<string>? escutaAntes = EscutaDasSagas;
		EscutaDasSagas = [];
		try
		{
			RoteiroDoCampeao(mesa, online, heroi);
		}
		catch (Exception e)
		{
			AfirmarCw("o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
		finally
		{
			CampeaoComAsBancadasDeTeste = false;
			EscutaDasSagas = escutaAntes;
			if (_corpoDoCampeao != 0 && _players.TryGetValue(_corpoDoCampeao, out ServerPlayer? corpo)) RemoverNpc(corpo);
			// TODOS FORA ANTES DE O PALCO DEVOLVER A PASTA: um `Drop` atrasado gravaria o personagem de bancada na pasta de verdade.
			foreach ((ClienteCruDeTeste cli, string conta) in new[] { (online, "online_cw"), (heroi, "heroi_cw") })
				if (cli.Conectado) mesa.SaiDoJogo(cli, conta);
			mesa.Fecha();
			palco.Dispose();
			// O MUNDO DE VERDADE DE VOLTA NA MEMORIA: as limpezas desta bancada zeraram tudo (a mesma devolucao do `NaCaixa` da `--wipeteste`).
			ZerarSoAMemoria();
			RecarregarMundoDoDisco();
			GD.Print($"[campeao] ================ {_cwOk} OK, {_cwFalhou} FALHA(S) ================");
		}
	}

	private CampeaoGuardado? CampeaoDoDisco()
	{
		CampeaoGuardado? naMemoria = _campeao;
		int corpo = _corpoDoCampeao;
		CarregarCampeaoDoWipe();
		CampeaoGuardado? lido = _campeao;
		_campeao = naMemoria;
		_corpoDoCampeao = corpo;
		return lido;
	}

	private void RoteiroDoCampeao(MesaDeClientesCrus mesa, ClienteCruDeTeste online, ClienteCruDeTeste heroi)
	{
		// ---------------------------------------------------------------- A. a limpeza guarda o mais forte
		GD.Print("[campeao] -- A) o mundo que acaba: o campeao, o segundo lugar, a bancada intrusa e o jogador online --");
		_campeao = null;
		_corpoDoCampeao = 0;
		var rng = new Random(94);

		CharacterSave campeao = SaveDeBancada("Campeao Velho", "Saiyan", LinhagemDe("Saiyan"), "Normal", false, rng);
		campeao.Ficha.BP = 4e9;
		campeao.FormasDespertadas = [Catalogo.Rede("ssj"), Catalogo.Rede("ssj2")];
		campeao.Skills = [SkillDoCampeaoA, SkillDoCampeaoB];
		var contaDoCampeao = new AccountSave { Conta = "campeao_cw", CriadaEm = NowMs() };
		contaDoCampeao.Slots[1] = campeao;   // na vaga 2: a vaga vai junto pro arquivo
		_store!.Gravar(contaDoCampeao);

		CharacterSave segundo = SaveDeBancada("Segundo Lugar", "Human", LinhagemDe("Human"), "Normal", false, rng);
		segundo.Ficha.BP = 2e9;
		var contaDoSegundo = new AccountSave { Conta = "segundo_cw", CriadaEm = NowMs() };
		contaDoSegundo.Slots[0] = segundo;
		_store.Gravar(contaDoSegundo);

		CharacterSave intrusa = SaveDeBancada("Bancada Intrusa", "Saiyan", LinhagemDe("Saiyan"), "Normal", false, rng);
		intrusa.Ficha.BP = 9e12;
		var contaIntrusa = new AccountSave { Conta = "bancada_cw_intrusa", CriadaEm = NowMs() };
		contaIntrusa.Slots[0] = intrusa;
		_store.Gravar(contaIntrusa);

		// O ONLINE: entra pela porta, e o DISCO dele passa a dizer 9 bi -- mas na memoria (o de agora) ele tem 1 bi. Guardar pelo disco
		// (o ultimo autosave) faria dele o campeao.
		ServerPlayer? on = mesa.EntraNoMundo(online, "online_cw", "Online");
		AfirmarCw("(montagem) o jogador online entrou pela porta", on != null);
		if (on == null) return;
		AccountSave? discoDoOnline = _store.Carregar("online_cw");
		if (discoDoOnline?.Slots[0] is { } salvo) { salvo.Ficha.BP = 9e9; _store.Gravar(discoDoOnline); }
		on.Ficha.BP = 1e9;

		ResultadoDaLimpeza r = ExecutarLimpeza(null);
		AfirmarCw("(montagem) a limpeza de producao rodou sem erro de disco", r.Erros.Count == 0, string.Join("; ", r.Erros));

		AfirmarCw("A1. a limpeza GUARDOU o campeao deste mundo: o personagem de maior BP base, inteiro",
				  _campeao is { Personagem.Nome: "Campeao Velho", ContaDeOrigem: "campeao_cw", Slot: 1 } a1 && Math.Abs(a1.BpBase - 4e9) < 1,
				  $"{_campeao?.Personagem.Nome} ({_campeao?.ContaDeOrigem}, vaga {_campeao?.Slot + 1}), BP base {_campeao?.BpBase:N0}");
		AfirmarCw("A2. ...e nao a conta de BANCADA de 9 trilhoes, nem o online pelo DISCO VELHO (9 bi no disco, 1 bi agora)",
				  _campeao?.Personagem.Nome == "Campeao Velho", $"{_campeao?.Personagem.Nome}");
		var sobraram = _store.TodosOsArquivos().Select(System.IO.Path.GetFileName).ToList();
		AfirmarCw("A3. a vassoura POUPOU o arquivo do campeao -- e ele e o unico que sobrou na pasta",
				  sobraram.Count == 1 && sobraram[0] == ArquivoDoCampeao, string.Join(", ", sobraram));
		CampeaoGuardado? lido = CampeaoDoDisco();
		AfirmarCw("A4. o arquivo relido e o campeao inteiro: o nome, o BP base, as skills e as formas que despertou, ESPERANDO o mundo novo",
				  lido is { Fase: FaseDoCampeao.Esperando, Personagem.Nome: "Campeao Velho" } a4 && Math.Abs(a4.BpBase - 4e9) < 1
				  && a4.Personagem.Skills.Contains(SkillDoCampeaoA) && a4.Personagem.Skills.Contains(SkillDoCampeaoB)
				  && a4.Personagem.FormasDespertadas.Contains(Catalogo.Rede("ssj2")),
				  lido == null ? "sem arquivo" : $"{lido.Personagem.Nome}, {lido.BpBase:N0}, {lido.Fase}, {lido.Personagem.Skills.Count} skill(s)");
		mesa.Bombeia(() => !online.Conectado, 4000);
		if (_campeao == null) return;

		// ---------------------------------------------------------------- B. os 50%
		GD.Print("[campeao] -- B) o mundo novo: 49% nao acorda, 50% acorda --");
		CampeaoComAsBancadasDeTeste = false;
		ServerPlayer? h = mesa.EntraNoMundo(heroi, "heroi_cw", "Heroi");
		AfirmarCw("(montagem) o heroi do mundo novo entrou pela porta", h != null);
		if (h == null) return;

		h.Ficha.BP = _campeao.BpBase * 0.49;
		TickDoCampeaoDoWipe();
		AfirmarCw("B1. com 49% do BP base do campeao, ele NAO desperta",
				  _campeao.Fase == FaseDoCampeao.Esperando && _corpoDoCampeao == 0, $"fase {_campeao.Fase}, corpo {_corpoDoCampeao}");

		EscutaDasSagas?.Clear();
		h.Ficha.BP = _campeao.BpBase * 0.5;
		TickDoCampeaoDoWipe();
		ServerPlayer? npc = _players.GetValueOrDefault(_corpoDoCampeao);
		AfirmarCw("B2. com 50%, ele DESPERTA", _campeao.Fase == FaseDoCampeao.NoMundo && npc != null, $"fase {_campeao.Fase}");
		if (npc == null) return;
		AfirmarCw("B3. ...NA TERRA, com o nome e a raca do save, e e um corpo do mundo (NPC), e nao um jogador",
				  npc.Zone.Name == PlanetaDoCampeao && npc.Name == "Campeao Velho" && npc.Race == "Saiyan" && EhNpcDoMundo(npc) && !EhJogador(npc),
				  $"{npc.Name} ({npc.Race}) em {npc.Zone.Name}");
		AfirmarCw("B4. ...com o BP BASE do campeao cravado, e as SKILLS dele",
				  Math.Abs(npc.Ficha.BP - 4e9) < 1 && npc.Livro.Sabe(SkillDoCampeaoA) && npc.Livro.Sabe(SkillDoCampeaoB),
				  $"BP {npc.Ficha.BP:N0}, {npc.Livro.Aprendidas.Count} skill(s)");
		AfirmarCw("B5. ...na FORMA MAIS FORTE que despertou (o SSJ2, e nao o SSJ)", npc.Forma.Atual == "ssj2", npc.Forma.Atual);
		AfirmarCw("B6. ...LETAL, com o Ki de chefe, cacando o planeta, e com o premio na bolsa",
				  npc.Combate.Letal && npc.Ficha.KiDeChefe && npc.Papel is { DaSaga: true, EhChefe: true } && npc.Ficha.Zeni == PremioDoCampeao,
				  $"letal {npc.Combate.Letal}, ki de chefe {npc.Ficha.KiDeChefe}, zeni {npc.Ficha.Zeni:N0}");
		AfirmarCw("B7. ...e o mundo OUVE: a chegada e o premio",
				  EscutaDasSagas is { } ouvido && ouvido.Any(l => l.Contains("DESPERTOU NA TERRA")) && ouvido.Any(l => l.Contains("Quem derrotar Campeao Velho ganha")),
				  string.Join(" | ", EscutaDasSagas ?? []));
		AfirmarCw("B8. ...e o arquivo ja diz 'na Terra'", CampeaoDoDisco()?.Fase == FaseDoCampeao.NoMundo, $"{CampeaoDoDisco()?.Fase}");

		// ---------------------------------------------------------------- C. o reinicio
		GD.Print("[campeao] -- C) o reinicio: o corpo nao atravessa, o arquivo sim --");
		RemoverNpc(npc);
		CarregarCampeaoDoWipe();
		AfirmarCw("C1. o disco relido diz 'na Terra', e o corpo e zero", _campeao is { Fase: FaseDoCampeao.NoMundo } && _corpoDoCampeao == 0);
		TickDoCampeaoDoWipe();
		npc = _players.GetValueOrDefault(_corpoDoCampeao);
		AfirmarCw("C2. o primeiro tique o poe DE VOLTA na Terra, na mesma forma",
				  npc != null && npc.Zone.Name == PlanetaDoCampeao && npc.Forma.Atual == "ssj2" && _campeao?.Fase == FaseDoCampeao.NoMundo,
				  npc == null ? $"sem corpo, fase {_campeao?.Fase}" : $"{npc.Zone.Name}, {npc.Forma.Atual}");
		if (npc == null || _campeao == null) return;

		// ---------------------------------------------------------------- D. a derrota
		GD.Print("[campeao] -- D) a derrota: o premio, o arquivo e a vez unica --");
		double zeniAntes = h.Ficha.Zeni;
		AoPerderALuta(npc, h, morreu: false);
		AfirmarCw("D1. o jogador que o derruba LEVA O PREMIO", Math.Abs(h.Ficha.Zeni - zeniAntes - PremioDoCampeao) < 1,
				  $"{zeniAntes:N0} -> {h.Ficha.Zeni:N0}");
		AfirmarCw("D2. ...e a fase vira 'derrotado' -- no arquivo tambem, com o nome de quem venceu",
				  _campeao.Fase == FaseDoCampeao.Derrotado && CampeaoDoDisco() is { Fase: FaseDoCampeao.Derrotado, VencidoPor: "Heroi" },
				  $"memoria {_campeao.Fase}, disco {CampeaoDoDisco()?.Fase}");
		int idDoCorpo = npc.Id;
		TickDoCampeaoDoWipe();
		AfirmarCw("D3. o corpo sai do mundo no tique seguinte", !_players.ContainsKey(idDoCorpo));
		h.Ficha.BP = _campeao.BpBase * 10;
		TickDoCampeaoDoWipe();
		TickDoCampeaoDoWipe();
		AfirmarCw("D4. ...e NAO volta neste wipe, nem com o heroi a dez vezes o BP dele",
				  _corpoDoCampeao == 0 && !_players.Values.Any(p => p.Papel?.Molde.Id == MoldeDoCampeao),
				  $"fase {_campeao.Fase}, corpo {_corpoDoCampeao}");

		// ---------------------------------------------------------------- E. a limpeza seguinte
		GD.Print("[campeao] -- E) a limpeza seguinte, sem ninguem acima do piso --");
		h.Ficha.BP = 50;
		ExecutarLimpeza(null);
		AfirmarCw("E1. a limpeza sem ninguem acima do piso MANTEM o campeao guardado",
				  _campeao?.Personagem.Nome == "Campeao Velho" && CampeaoDoDisco()?.Personagem.Nome == "Campeao Velho",
				  $"{_campeao?.Personagem.Nome}");
		AfirmarCw("E2. ...e ele volta a ESPERAR o mundo novo (no arquivo tambem)",
				  _campeao?.Fase == FaseDoCampeao.Esperando && CampeaoDoDisco()?.Fase == FaseDoCampeao.Esperando, $"{_campeao?.Fase}");
		mesa.Bombeia(() => !heroi.Conectado, 4000);

		// ---------------------------------------------------------------- F. o admin
		GD.Print("[campeao] -- F) o verbo do admin --");
		var adm = new ServerPlayer { Id = -940_001, Peer = null, Name = "Admin da bancada", Conta = "adm_cw", Ficha = new Fighter() };
		AdminCampeaoDoWipe(adm);
		npc = _players.GetValueOrDefault(_corpoDoCampeao);
		AfirmarCw("F1. o verbo do admin o chama na Terra na hora, sem os 50%",
				  npc != null && npc.Zone.Name == PlanetaDoCampeao && _campeao?.Fase == FaseDoCampeao.NoMundo, $"fase {_campeao?.Fase}");
	}
}
