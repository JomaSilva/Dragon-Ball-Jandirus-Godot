using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.Races;
using Jandirus.Core.Social;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--recicladoteste`, AS FAMILIAS M -- O QUE O MUNDO GUARDAVA POR QUEM SAIU DA VAGA (dono, 2026-10-19). As decisoes e o conserto
/// estao no cabecalho de `GameServer.VagaReciclada.cs`. Dois clientes crus a mais na mesma mesa: o HERDEIRO (a vaga que e apagada e
/// recriada pela tela de selecao, quatro vezes) e o CLADO (outra conta: o pai, quem empresta as Super Esferas, o Namekuseijin do Cla do
/// Dragao). O que a bancada planta pela assinatura da vaga e tirado no `finally`, e as Super Esferas voltam como estavam.
///
///   MV. O CONTRA-EXEMPLO, com os seis defeitos ligados: o Antigo e apagado e o Herdeiro nasce na MESMA vaga -- e e mae da gravidez
///       dela, dono do dominio, quem o tanque de bio consome, dono das Super Esferas, criador da estatua e barrado nas esperas. E o
///       jogo de antes, pelas portas de verdade.
///   M1. O CONSERTO, o Herdeiro apagado: o mundo com a vaga VAZIA (a gravidez se perdeu, o ovo e o filho do pai ficaram orfaos, o
///       dominio caiu com os recados e a invasao contra ele, o tanque espera um criador que nao volta, as Super Esferas ficaram livres,
///       a estatua dorme sem criador, as esperas sairam) -- e o Terceiro, nascido nela, sem nada disso.
///   M2. A ESTATUA SEM CRIADOR: um Humano nao a desperta nem ve o botao; o Clado, Namekuseijin do Cla do Dragao, ve "Despertar as
///       esferas", a desperta e vira o criador (e com `EstatuaSemCriadorSoOCriadorDespertaDeTeste` ninguem a desperta).
///   M3. AS SUPER ESFERAS POR PROCURACAO: o Terceiro guarda as sete pelo Clado e e apagado -- elas voltam pro Clado e a procuracao acaba.
///   M4. O PEDINTE SAI: o Clado guarda as sete pelo Quarto, e o Quarto e apagado -- a procuracao cai e as sete ficam com o Clado.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O que a bancada M plantou pela vaga, pra tirar no fim. Ids e chaves: o que o conserto mexe, ela acha de novo.</summary>
	private sealed class PlantioDaVaga
	{
		public readonly List<int> Bebes = [];
		public readonly List<Dominio> Dominios = [];
		public readonly List<ChaveDePlaneta> Invasoes = [];
		public readonly List<Obra> Labs = [];
		public readonly List<SetDeEsferas> Sets = [];
		public readonly List<string> Sigs = [];
		public bool TorneioPlantado;
	}

	private static readonly ChaveDePlaneta DominioDaBancadaM = new(true, "Earth", 0);
	private static readonly ChaveDePlaneta InvasaoDaBancadaM = new(false, "MundoDaBancadaDoReciclado", 4_242_424_242UL);

	private static void LigarHerancaDaVagaDeTeste(bool ligar)
	{
		BebesHerdamAVagaDeTeste = DominiosHerdamAVagaDeTeste = FornadaHerdaAVagaDeTeste = ligar;
		SuperEsferasHerdamAVagaDeTeste = EstatuaHerdaAVagaDeTeste = EsperasHerdamAVagaDeTeste = ligar;
	}

	private void RoteiroDoMundoReciclado(MesaDeClientesCrus mesa, ClienteCruDeTeste herdeiro, ClienteCruDeTeste cla)
	{
		GD.Print("[reciclado] -- M) o que o mundo guardava pela vaga: bebes, dominios, tanque, Super Esferas, estatua, esperas --");
		string contaH = ContaRc("herdeiro"), contaC = ContaRc("cla");
		ServerPlayer? antigo = mesa.EntraNoMundo(herdeiro, contaH, "Antigo");
		ServerPlayer? clado = mesa.EntraNoMundo(cla, contaC, "Clado");
		AfirmarRec("(montagem M) o Antigo e o Clado entraram pela porta, e nenhum mundo deles tem dominio nem estatua",
				  antigo != null && clado != null && DominioDe(DominioDaBancadaM) == null && !_sets.Exists(s => s.Zona.Hash == antigo.Zone.Hash));
		if (antigo == null || clado == null || DominioDe(DominioDaBancadaM) != null || _sets.Exists(s => s.Zona.Hash == antigo.Zone.Hash)) return;

		string sig = antigo.Assinatura, sigC = clado.Assinatura;
		ZoneKey mundo = antigo.Zone;
		Vec2 ondeEstatua = antigo.Pos;
		var supersAntes = _supers.Select(s => (s.Numero, s.Dono, s.DonoNome)).ToList();
		(string, string, string, string) procuracaoAntes = (_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido);
		(string Race, string Class) cladoAntes = (clado.Race, clado.Class);
		var plantio = new PlantioDaVaga();
		try
		{
			// ---------------------------------------------------------------- MV. o contra-exemplo: o jogo de antes
			GD.Print("[reciclado] -- MV) o CONTRA-EXEMPLO: o Herdeiro nasce na vaga do Antigo e herda o mundo dele --");
			LigarHerancaDaVagaDeTeste(true);
			ServerPlayer? n1;
			try
			{
				PlantarNaVaga(plantio, sig, "Antigo", contaH, sigC, mundo, ondeEstatua);
				bool apagou = ApagarPelaSelecao(mesa, herdeiro, contaH, "Antigo");
				bool barrado = _proximaCampanha.ContainsKey(sig) && _torneioDoCell is { } tv && tv.JaLutaram.Contains(sig) && tv.Barrados.Contains(sig);
				bool recadoEsperava = _recadosDeConquista.ContainsKey(sig);
				TirarOTorneioPlantado(plantio);
				n1 = CriarNaVaga(mesa, herdeiro, contaH, "Herdeiro");
				AfirmarRec("(precondicao M) o Herdeiro nasce na MESMA vaga, com a MESMA assinatura do Antigo", apagou && n1?.Assinatura == sig,
						  n1?.Assinatura ?? "nao entrou");
				if (n1 == null) return;
				AfirmarRec("(defeito injetado: a vaga reciclada nao mexe nos bebes) o Herdeiro e a MAE da gravidez do Antigo -- a regua M1 REPROVA",
						  BebeDaMae(n1.Assinatura) is { DeOvo: false, Mae: "Antigo" });
				AfirmarRec("(defeito injetado: os dominios ficam na vaga) o Herdeiro e o DONO do dominio do Antigo, e leu no login o recado guardado "
						  + "pra ele -- a regua M1 REPROVA",
						  DominiosDe(n1.Assinatura).Count == 1 && recadoEsperava && !_recadosDeConquista.ContainsKey(n1.Assinatura));
				AfirmarRec("(defeito injetado: o tanque fica na vaga) o Herdeiro e quem o tanque de bio do Antigo CONSOME no parto -- a regua M1 REPROVA",
						  plantio.Labs.Count == 1 && plantio.Labs[0].Fornada is { } gv && EhOCriadorDaFornada(gv, n1));
				AfirmarRec("(defeito injetado: as Super Esferas ficam na vaga) o Herdeiro tem as tres Super Esferas do Antigo -- a regua M1 REPROVA",
						  QuantasSupersTem(n1.Assinatura) == 3);
				AfirmarRec("(defeito injetado: a estatua fica na vaga) o Herdeiro e o CRIADOR da estatua do Antigo, de pe -- a regua M1 REPROVA",
						  plantio.Sets.Count == 1 && plantio.Sets[0] is { Inerte: false } sv && sv.CriadorSig == n1.Assinatura);
				AfirmarRec("(defeito injetado: as esperas ficam na vaga) a vaga continua sem poder invadir e barrada no Torneio do Cell -- a regua M1 REPROVA",
						  barrado);
			}
			finally
			{
				LigarHerancaDaVagaDeTeste(false);
				ArrancarOPlantio(plantio);
				VoltarAsSuperEsferas(supersAntes, procuracaoAntes);
			}

			// ---------------------------------------------------------------- M1. o conserto
			GD.Print("[reciclado] -- M1) o Herdeiro e apagado pela tela de selecao: o mundo esquece a vaga --");
			PlantarNaVaga(plantio, sig, "Herdeiro", contaH, sigC, mundo, ondeEstatua);
			Obra lab = plantio.Labs[0];
			long prometida = lab.Fornada!.PrometidaEm;
			SetDeEsferas estatua = plantio.Sets[0];
			bool apagou1 = ApagarPelaSelecao(mesa, herdeiro, contaH, "Herdeiro");
			AfirmarRec("(precondicao M1) a vaga do Herdeiro ficou vazia", apagou1);

			BebeACaminho? Bebe(int i) => _bebes.Bebes.Find(b => b.Id == plantio.Bebes[i]);
			AfirmarRec("**M1 A GRAVIDEZ MORRE COM A MAE** (no DM ela morava no corpo dela): sai da fila", Bebe(0) == null);
			AfirmarRec("**M1 O OVO FICA ORFAO**: continua na fila, com o nome de quem o pos, e nao aponta mais pra vaga",
					  Bebe(1) is { DeOvo: true, SigMae: "", Mae: "Herdeiro" }, $"{Bebe(1)?.SigMae}/{Bebe(1)?.Mae}");
			AfirmarRec("**M1 O FILHO DO PAI QUE SAIU FICA ORFAO DE PAI**: continua na fila com a mae dele, sem apontar pra vaga",
					  Bebe(2) is { SigPai: "", Pai: "Herdeiro" } b2 && b2.SigMae == sigC, $"{Bebe(2)?.SigPai}/{Bebe(2)?.SigMae}");
			AfirmarRec("**M1 O DOMINIO VOLTA PRO MUNDO NA HORA**: a bandeira caiu, o planeta esta livre", DominioDe(DominioDaBancadaM) == null
					  && DominiosDe(sig).Count == 0);
			AfirmarRec("**M1 OS RECADOS GUARDADOS PRO ANTIGO SOMEM** (o novo da vaga os leria no login)", !_recadosDeConquista.ContainsKey(sig));
			AfirmarRec("**M1 A INVASAO CONTRA O DOMINIO DELE NAO MANDA MAIS RECADO PRA VAGA**",
					  _invasoes.TryGetValue(InvasaoDaBancadaM, out Invasao? inv) && inv.DonoAntigoAssinatura == "" && inv.DonoAntigoNome == "Herdeiro");
			AfirmarRec("**M1 O TANQUE ESPERA PRA SEMPRE**: a fornada continua gestando, com um criador que nao volta",
					  lab.Fornada is { } g1 && g1.DonoAssinatura == CriadorQueNaoVolta && g1.PrometidaEm == prometida && _noChao.Contains(lab),
					  lab.Fornada?.DonoAssinatura ?? "sem fornada");
			AfirmarRec("**M1 AS SUPER ESFERAS DELE FICAM LIVRES**", _supers.Count(s => s.Numero <= 3 && s.Dono.Length == 0) == 3,
					  string.Join(",", _supers.Where(s => s.Numero <= 3).Select(s => $"#{s.Numero}={s.DonoNome}")));
			AfirmarRec("**M1 A ESTATUA DORME SEM CRIADOR**: inerte, e o nome de quem a ergueu fica como historia",
					  estatua is { Inerte: true, CriadorSig: "", CriadorNome: "Herdeiro" } && _sets.Contains(estatua));
			AfirmarRec("**M1 A ESPERA ENTRE INVASOES NAO FICA NA VAGA**", !_proximaCampanha.ContainsKey(sig));
			AfirmarRec("**M1 O TORNEIO DO CELL ESQUECE A VAGA** (ja lutou / se meteu)",
					  _torneioDoCell is { } t1 && !t1.JaLutaram.Contains(sig) && !t1.Barrados.Contains(sig));
			TirarOTorneioPlantado(plantio);

			ServerPlayer? n2 = CriarNaVaga(mesa, herdeiro, contaH, "Terceiro");
			AfirmarRec("(precondicao M1) o Terceiro nasce na mesma vaga", n2?.Assinatura == sig, n2?.Assinatura ?? "nao entrou");
			if (n2 == null) return;
			AfirmarRec("...e o TERCEIRO nasce sem nada do Herdeiro: nao e mae nem pai de bebe nenhum, nao domina nada, nao e o criador do "
					  + "tanque, nao tem Super Esfera, nao ergueu a estatua e pode invadir",
					  BebeDaMae(n2.Assinatura) == null && !_bebes.Bebes.Exists(b => b.SigPai == n2.Assinatura) && DominiosDe(n2.Assinatura).Count == 0
					  && lab.Fornada is { } g2 && !EhOCriadorDaFornada(g2, n2) && QuantasSupersTem(n2.Assinatura) == 0
					  && estatua.CriadorSig != n2.Assinatura && !_proximaCampanha.ContainsKey(n2.Assinatura));

			// ---------------------------------------------------------------- M2. a estatua sem criador
			GD.Print("[reciclado] -- M2) a estatua sem criador: so outro Namekuseijin do Cla do Dragao a desperta --");
			if (clado.Zone.Hash != mundo.Hash) MoveToZone(clado.Id, mundo, ondeEstatua + new Vec2(ZoneCollision.TileSize, 0));
			bool BotaoDeDespertar(ServerPlayer p) =>
				RequisitosDosVerbos.Linha(RequisitosDosVerbos.ChaveDoVerbo("db_reviver")) is { } l && l.Requisito.Atende(PosseDe(p));
			bool humanoVe = BotaoDeDespertar(clado);
			ReviverOSet(clado);
			AfirmarRec("**M2 UM HUMANO NAO DESPERTA A ESTATUA SEM CRIADOR**, e nem ve o botao", !humanoVe && estatua.Inerte && estatua.CriadorSig == "");
			clado.Race = "Namekian";
			clado.Class = "Dragon clan";
			EstatuaSemCriadorSoOCriadorDespertaDeTeste = true;
			try
			{
				bool viuComDefeito = BotaoDeDespertar(clado);
				ReviverOSet(clado);
				AfirmarRec("(defeito injetado: a estatua sem criador so acorda pelo criador) o Cla do Dragao nao ve o botao e nao a desperta -- a "
						  + "regua de baixo REPROVA", !viuComDefeito && estatua.Inerte && estatua.CriadorSig == "");
			}
			finally { EstatuaSemCriadorSoOCriadorDespertaDeTeste = false; }
			AfirmarRec("**M2 O CLA DO DRAGAO VE \"Despertar as esferas\"** num mundo cuja estatua perdeu o criador (o fato e o botao)",
					  (FatosDe(clado) & Fatos.EstatuaSemCriadorAqui) != 0 && BotaoDeDespertar(clado));
			ReviverOSet(clado);
			AfirmarRec("**M2 O CLA DO DRAGAO DESPERTA A ESTATUA E VIRA O CRIADOR DELA**",
					  estatua is { Inerte: false } && estatua.CriadorSig == sigC && estatua.CriadorNome == "Clado"
					  && (FatosDe(clado) & Fatos.CriouAEstatuaDaqui) != 0, $"inerte={estatua.Inerte} criador={estatua.CriadorNome}");

			// ---------------------------------------------------------------- M3. quem guardava por procuracao sai
			GD.Print("[reciclado] -- M3) o Terceiro guarda as sete pelo Clado e e apagado --");
			foreach (SuperEsfera s in _supers) { s.Dono = sig; s.DonoNome = "Terceiro"; }
			(_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido) = (sigC, "Clado", "", "");
			bool apagou3 = ApagarPelaSelecao(mesa, herdeiro, contaH, "Terceiro");
			AfirmarRec("**M3 AS SETE QUE ELE GUARDAVA POR PROCURACAO VOLTAM PRA QUEM EMPRESTOU**, e a procuracao acaba",
					  apagou3 && QuantasSupersTem(sigC) == SuperEsferas.Total && !HaProcuracao, $"{QuantasSupersTem(sigC)}/7 benef={_benefNome}");

			// ---------------------------------------------------------------- M4. o pedinte sai
			GD.Print("[reciclado] -- M4) o Clado guarda as sete pelo Quarto, e o Quarto e apagado --");
			ServerPlayer? n3 = CriarNaVaga(mesa, herdeiro, contaH, "Quarto");
			if (n3 == null) { AfirmarRec("(precondicao M4) o Quarto nasce na vaga", false); return; }
			foreach (SuperEsfera s in _supers) { s.Dono = sigC; s.DonoNome = "Clado"; }
			(_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido) = (sig, "Quarto", "", "");
			bool apagou4 = ApagarPelaSelecao(mesa, herdeiro, contaH, "Quarto");
			AfirmarRec("**M4 O PEDINTE SAI: A PROCURACAO CAI** e as sete ficam com quem as guarda",
					  apagou4 && !HaProcuracao && QuantasSupersTem(sigC) == SuperEsferas.Total, $"benef={_benefNome}");
		}
		finally
		{
			LigarHerancaDaVagaDeTeste(false);
			EstatuaSemCriadorSoOCriadorDespertaDeTeste = false;
			ArrancarOPlantio(plantio);
			VoltarAsSuperEsferas(supersAntes, procuracaoAntes);
			(clado.Race, clado.Class) = cladoAntes;
		}
	}

	/// <summary>
	/// PLANTA NA VAGA tudo o que o mundo guarda por assinatura: a gravidez, o ovo e o filho de que ela e pai; o dominio, um recado e uma
	/// invasao contra ele; o tanque fechado; as Super Esferas #1..#3; a estatua; a espera entre invasoes e o Torneio do Cell (de mentira, so
	/// pelas listas -- nenhum tique roda durante a bancada).
	/// </summary>
	private void PlantarNaVaga(PlantioDaVaga p, string sig, string nome, string conta, string sigOutro, ZoneKey mundo, Vec2 onde)
	{
		p.Sigs.Add(sig);
		BebeACaminho Bebe(bool ovo, string mae, string sigMae, string pai, string sigPai) => new()
		{
			Id = _bebes.Proximo++, ConcebidoEm = NowMs(), DeOvo = ovo, Mae = mae, SigMae = sigMae, Pai = pai, SigPai = sigPai,
			Raca = "Human", RacaDaMae = "Human", RacaDoPai = ovo ? "" : "Human",
		};
		foreach (BebeACaminho b in new[] { Bebe(false, nome, sig, "Clado", sigOutro), Bebe(true, nome, sig, "", ""), Bebe(false, "Clada", sigOutro, nome, sig) })
		{
			_bebes.Bebes.Add(b);
			p.Bebes.Add(b.Id);
		}
		SalvarBebes();

		var d = new Dominio
		{
			PreFeito = true, Planeta = DominioDaBancadaM.Nome, Assinatura = sig, Conta = conta, Nome = nome,
			Desde = TempoDoMundo, Coletado = TempoDoMundo, Visto = TempoDoMundo, Fx = onde.X, Fy = onde.Y,
		};
		_dominios.Add(d);
		p.Dominios.Add(d);
		_recadosDeConquista[sig] = [$"recado de bancada pra {nome}"];
		_invasoes[InvasaoDaBancadaM] = new Invasao
		{
			Chave = InvasaoDaBancadaM, Planeta = InvasaoDaBancadaM.Nome, DonoAntigoAssinatura = sig, DonoAntigoNome = nome, DonoAntigoConta = conta,
		};
		p.Invasoes.Add(InvasaoDaBancadaM);
		SalvarConquista();

		var lab = new Obra
		{
			Id = -9_242, Tipo = "bancada_do_reciclado", Lab = 2, Aparafusada = true, DonoConta = conta, DonoNome = nome,
			Fornada = new Gestacao
			{
				DonoConta = conta, DonoAssinatura = sig, PrometidaEm = NowMs() + 3_600_000,
				Amostras = { new Amostra { Raca = "Human", Doador = "doador da bancada", Bp = 1000 } },
			},
		};
		_noChao.Add(lab);
		p.Labs.Add(lab);

		foreach (SuperEsfera s in _supers)
			if (s.Numero <= 3) { s.Dono = sig; s.DonoNome = nome; }
		SalvarSupers();

		var set = new SetDeEsferas
		{
			Id = ProximoIdDeSet(), Ex = onde.X, Ey = onde.Y, CriadorSig = sig, CriadorConta = conta, CriadorNome = nome,
			Poder = 1000, Desejos = 1, OffTime = Esferas.OffTimeDeJogador,
		};
		set.PorZona(mundo);
		_sets.Add(set);
		p.Sets.Add(set);
		SalvarEsferas();

		_proximaCampanha[sig] = _relogioDoMundo + Conquista.SegundosEntreCampanhas;
		if (_torneioDoCell == null) { _torneioDoCell = new TorneioDoCellVivo(); p.TorneioPlantado = true; }
		_torneioDoCell.JaLutaram.Add(sig);
		_torneioDoCell.Barrados.Add(sig);
	}

	/// <summary>O Torneio do Cell de mentira sai ANTES de alguem entrar no mundo: o login nao pode achar um Cell que nao existe.</summary>
	private void TirarOTorneioPlantado(PlantioDaVaga p)
	{
		if (p.TorneioPlantado) _torneioDoCell = null;
		else if (_torneioDoCell is { } t) foreach (string s in p.Sigs) { t.JaLutaram.Remove(s); t.Barrados.Remove(s); }
		p.TorneioPlantado = false;
	}

	/// <summary>Tira tudo o que a bancada M plantou -- o que o conserto ja tirou, ou o que o defeito deixou.</summary>
	private void ArrancarOPlantio(PlantioDaVaga p)
	{
		TirarOTorneioPlantado(p);
		_bebes.Bebes.RemoveAll(b => p.Bebes.Contains(b.Id));
		SalvarBebes();
		foreach (Dominio d in p.Dominios) _dominios.Remove(d);
		foreach (ChaveDePlaneta c in p.Invasoes) _invasoes.Remove(c);
		foreach (string s in p.Sigs) { _recadosDeConquista.Remove(s); _proximaCampanha.Remove(s); }
		SalvarConquista();
		foreach (Obra lab in p.Labs) _noChao.Remove(lab);
		foreach (SetDeEsferas s in p.Sets)
		{
			_esferas.RemoveAll(e => e.Set == s.Id);
			_sets.Remove(s);
			MandarEsferas(s.Zona);
		}
		SalvarEsferas();
		p.Bebes.Clear(); p.Dominios.Clear(); p.Invasoes.Clear(); p.Labs.Clear(); p.Sets.Clear(); p.Sigs.Clear();
	}

	/// <summary>As Super Esferas e a procuracao voltam como estavam antes da bancada.</summary>
	private void VoltarAsSuperEsferas(List<(int Numero, string Dono, string DonoNome)> antes, (string, string, string, string) procuracao)
	{
		foreach (SuperEsfera s in _supers)
			if (antes.Find(a => a.Numero == s.Numero) is { Numero: > 0 } a) { s.Dono = a.Dono; s.DonoNome = a.DonoNome; }
		(_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido) = procuracao;
		SalvarSupers();
	}
}
