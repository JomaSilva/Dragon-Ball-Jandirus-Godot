using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--recicladoteste` -- QUEM SAI DA VAGA SAI DA ABA PEOPLE DE TODO MUNDO (dono, 2026-10-19). O pedido, a causa e o conserto
/// estao no cabecalho de `GameServer.ConvivioReciclado.cs`. Os personagens entram pela PORTA (clientes crus), numa pasta de saves
/// TEMPORARIA (o `PalcoDeApagamentos`: apagar e reencarnar gravam de verdade, so que noutro lugar):
///
///   0. A VARREDURA RODOU NO BOOT deste processo -- e a linha do boot que a chama.
///   A. A MONTAGEM: o Amigo (no mundo) e amigo declarado do Velho, da Vitima e do Longe; o Longe (fora do jogo, so no disco) e o Tela
///      (parado na tela de selecao, copia viva da conta) conhecem o Velho e a Vitima.
///   V. O CONTRA-EXEMPLO (`ConvivioHerdaAAssinaturaDeTeste`): a Vitima e apagada pela tela de selecao e recriada na MESMA vaga -- e o
///      Amigo a reencontra com o convivio e a declaracao da antiga. E o jogo de antes, reproduzido inteiro.
///   R. A REENCARNACAO pelo funil de producao do Enma (`ApagarOPersonagemParaSempre`): o Amigo esquece o Velho na hora (no servidor e
///      na aba que chegou pelo fio), o Longe no disco, o Tela na copia da selecao e no disco -- e o Amigo continua conhecendo o Longe,
///      que nao saiu de vaga nenhuma. O Velho volta pela criacao na mesma vaga: a lista dele nasce vazia, e o Amigo o reencontra do zero.
///   D. O APAGAMENTO pela tela de selecao, sem defeito: o Amigo esquece a Vitima (que carregava a ficha herdada do V).
///   C. A CRIACAO na vaga vazia limpa o resto de antes do conserto: fichas plantadas da assinatura da vaga saem quando alguem nasce nela.
///   B. A VARREDURA DO BOOT, sobre saves plantados: saem a ficha da vaga vazia, a de antes do nascimento e a sem data de quem nasceu
///      depois do corte; ficam a de depois do nascimento, a sem data de quem nasceu antes do corte e a de assinatura que ninguem tem --
///      e o CONTRA-EXEMPLO (`ConvivioVelhoFicaDeTeste`) nao tira nada.
///   M. (2026-10-19, depois) O RESTO DO QUE O MUNDO GUARDAVA PELA VAGA -- bebes, dominios, o tanque de bio, as Super Esferas, a estatua,
///      as esperas --, com o Herdeiro e o Clado: ver `GameServer.RecicladoMundoTeste.cs`.
///
///     Godot --headless --path . --server --port 8760 --recicladoteste
/// </summary>
public sealed partial class GameServer
{
	private bool _recicladoJaRodou;
	private int _recOk, _recFalhou;

	private void AfirmarRec(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _recOk++; GD.Print($"[reciclado]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _recFalhou++; GD.PrintErr($"[reciclado]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoReciclado(int porta)
	{
		if (_recicladoJaRodou) return;
		_recicladoJaRodou = true;
		_recOk = _recFalhou = 0;
		GD.Print("[reciclado] ================ QUEM SAI DA VAGA SAI DA ABA PEOPLE (dono, 2026-10-19) ================");

		// ---- 0. a linha do boot ----
		AfirmarRec("0. A VARREDURA DO CONVIVIO RODOU NO BOOT deste processo (e a linha do boot que a chama)", VarreduraDoConvivio.Rodou,
				  $"contas={VarreduraDoConvivio.Contas} fichas={VarreduraDoConvivio.Fichas}");

		PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada();
		var amigo = new ClienteCruDeTeste();
		var velho = new ClienteCruDeTeste();
		var vitima = new ClienteCruDeTeste();
		var longe = new ClienteCruDeTeste();
		var tela = new ClienteCruDeTeste();
		var herdeiro = new ClienteCruDeTeste();
		var cla = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, amigo, velho, vitima, longe, tela, herdeiro, cla);
		try
		{
			RoteiroDoReciclado(mesa, amigo, velho, vitima, longe, tela);
			RoteiroDoMundoReciclado(mesa, herdeiro, cla);
		}
		catch (Exception e)
		{
			AfirmarRec("o roteiro correu ate o fim sem excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
		finally
		{
			ConvivioHerdaAAssinaturaDeTeste = false;
			ConvivioVelhoFicaDeTeste = false;
			// TODOS FORA ANTES DE O PALCO DEVOLVER A PASTA: um `Drop` atrasado gravaria o personagem de bancada na pasta de verdade.
			foreach ((ClienteCruDeTeste cli, string conta) in new[] { (amigo, ContaRc("amigo")), (velho, ContaRc("velho")),
					   (vitima, ContaRc("vitima")), (longe, ContaRc("longe")), (tela, ContaRc("tela")),
					   (herdeiro, ContaRc("herdeiro")), (cla, ContaRc("cla")) })
				if (cli.Conectado) mesa.SaiDoJogo(cli, conta);
			mesa.Fecha();
			palco.Dispose();
			GD.Print($"[reciclado] ================ {_recOk} OK, {_recFalhou} FALHA(S) ================");
		}
	}

	private static string ContaRc(string quem) => "bancada_rc_" + quem;

	private void RoteiroDoReciclado(MesaDeClientesCrus mesa, ClienteCruDeTeste amigo, ClienteCruDeTeste velho, ClienteCruDeTeste vitima,
									ClienteCruDeTeste longe, ClienteCruDeTeste tela)
	{
		// ---------------------------------------------------------------- A. a montagem
		GD.Print("[reciclado] -- A) a montagem: o Amigo, o Velho, a Vitima, o Longe (fora do jogo) e o Tela (na selecao) --");
		ServerPlayer? a = mesa.EntraNoMundo(amigo, ContaRc("amigo"), "Amigo");
		ServerPlayer? b = mesa.EntraNoMundo(velho, ContaRc("velho"), "Velho");
		ServerPlayer? v = mesa.EntraNoMundo(vitima, ContaRc("vitima"), "Vitima");
		ServerPlayer? l = mesa.EntraNoMundo(longe, ContaRc("longe"), "Longe");
		ServerPlayer? t = mesa.EntraNoMundo(tela, ContaRc("tela"), "Tela");
		AfirmarRec("(montagem) os cinco entraram pela porta, cada um com o seu personagem na vaga 1",
				  a != null && b != null && v != null && l != null && t != null);
		if (a == null || b == null || v == null || l == null || t == null) return;
		string sigB = b.Assinatura, sigV = v.Assinatura, sigL = l.Assinatura;

		// AMIZADE DE VERDADE: convivio de amigo velho e a declaracao, com o "desde quando" de agora (depois de todos nascerem).
		void Amizade(ServerPlayer de, ServerPlayer com)
		{
			DeclararDeTeste(de, com, Relacao.MuitoBom);
			Conhecido ficha = de.Social.Ficha(com.Assinatura)!;
			ficha.Familiaridade = 150;
			ficha.DesdeMs = ficha.VistoEm = NowMs();
		}
		Amizade(a, b); Amizade(a, v); Amizade(a, l);
		Amizade(b, a);
		Amizade(l, b); Amizade(l, v);
		Amizade(t, b); Amizade(t, v);
		MandarConhecidos(a);

		// O LONGE SAI DO JOGO (a lista dele vai pro disco pelo `Drop` de producao); o TELA sai e volta so ate a selecao.
		bool longeSaiu = mesa.SaiDoJogo(longe, ContaRc("longe"));
		bool telaNaSelecao = mesa.SaiDoJogo(tela, ContaRc("tela")) && mesa.Entra(tela) && mesa.Loga(tela, ContaRc("tela"));
		bool chegouAAba = mesa.Bombeia(() => amigo.Conhecidos?.Any(k => k.Sig == sigV) == true, 3000);
		AfirmarRec("(montagem) o Amigo e amigo declarado do Velho, da Vitima e do Longe -- no servidor e na aba que chegou pelo fio",
				  new[] { sigB, sigV, sigL }.All(s => a.Social.RelacaoCom(s) == Relacao.MuitoBom && a.Social.Familiaridade(s) == 150) && chegouAAba);
		AfirmarRec("(montagem) o Longe saiu do jogo com o Velho e a Vitima na lista do disco",
				  longeSaiu && ListaNoDisco(ContaRc("longe")) is { } ll && ll.Conhece(sigB) && ll.Conhece(sigV));
		AfirmarRec("(montagem) o Tela esta na tela de selecao, com o Velho e a Vitima na copia viva da conta e no disco",
				  telaNaSelecao && ListaViva(ContaRc("tela")) is { } lt && lt.Conhece(sigB) && lt.Conhece(sigV)
				  && ListaNoDisco(ContaRc("tela")) is { } dt && dt.Conhece(sigB) && dt.Conhece(sigV));

		// ---------------------------------------------------------------- V. o contra-exemplo: o jogo de antes
		GD.Print("[reciclado] -- V) o CONTRA-EXEMPLO: a vaga reciclada sem o conserto --");
		ConvivioHerdaAAssinaturaDeTeste = true;
		ServerPlayer? v2;
		try
		{
			bool apagou = ApagarPelaSelecao(mesa, vitima, ContaRc("vitima"), "Vitima");
			bool conheciaAinda = a.Social.Conhece(sigV);
			v2 = CriarNaVaga(mesa, vitima, ContaRc("vitima"), "VitimaDois");
			if (v2 != null) Fotografar(a, v2, NowMs() + 61_000);   // o reencontro (passado o minuto da foto)
			Conhecido? herdada = a.Social.Ficha(sigV);
			AfirmarRec("(defeito injetado: a vaga reciclada nao tira a assinatura de lista nenhuma) a Vitima apagada CONTINUA na lista do "
					  + "Amigo, e a Vitima NOVA da mesma vaga e reencontrada com o convivio e a declaracao da antiga -- a regua do R REPROVA",
					  apagou && conheciaAinda && v2 != null && v2.Assinatura == sigV
					  && herdada is { Familiaridade: >= 150, Relacao: Relacao.MuitoBom, Nome: "VitimaDois" },
					  $"apagou={apagou} conhecia={conheciaAinda} ficha={herdada?.Nome}/{herdada?.Familiaridade}/{herdada?.Relacao}");
		}
		finally { ConvivioHerdaAAssinaturaDeTeste = false; }
		if (v2 == null) return;

		// ---------------------------------------------------------------- R. a reencarnacao
		GD.Print("[reciclado] -- R) o Velho reencarna pelo Enma --");
		var eco = new Reencarnacao { De = b.Name, BPAntigo = b.Ficha.BP, Fracao = Alem.FracaoDaReencarnacao };
		ApagarOPersonagemParaSempre(b, "reencarnou pelo Enma", eco);
		mesa.Bombeia(() => !velho.Conectado, 4000);
		bool abaSemOVelho = mesa.Bombeia(() => amigo.Conhecidos is { } k && k.All(c => c.Sig != sigB), 3000);
		AfirmarRec("**O AMIGO ESQUECE O VELHO NA HORA DA REENCARNACAO** -- no servidor, e a aba dele chega pelo fio sem o Velho",
				  !a.Social.Conhece(sigB) && abaSemOVelho, $"servidor={a.Social.Conhece(sigB)} aba={abaSemOVelho}");
		AfirmarRec("...e quem nao saiu de vaga nenhuma continua la: o Longe segue amigo declarado do Amigo",
				  a.Social.RelacaoCom(sigL) == Relacao.MuitoBom && amigo.Conhecidos?.Any(k => k.Sig == sigL) == true);
		AfirmarRec("...o LONGE, fora do jogo, esquece o Velho no DISCO", ListaNoDisco(ContaRc("longe")) is { } l2 && !l2.Conhece(sigB) && l2.Conhece(sigV));
		AfirmarRec("...o TELA, na tela de selecao, esquece o Velho na COPIA VIVA da conta e no disco (a copia viva regravaria o arquivo)",
				  ListaViva(ContaRc("tela")) is { } t2 && !t2.Conhece(sigB) && ListaNoDisco(ContaRc("tela")) is { } t3 && !t3.Conhece(sigB));

		ServerPlayer? novo = CriarNaVaga(mesa, velho, ContaRc("velho"), "Novo");
		AfirmarRec("(precondicao) o Velho volta pela criacao na MESMA vaga, e a vaga devolve a MESMA assinatura -- a raiz do pedido",
				  novo != null && novo.Assinatura == sigB, novo?.Assinatura ?? "nao entrou");
		if (novo == null) return;
		AfirmarRec("...a lista do reencarnado nasce VAZIA", novo.Social.Conhecidos.Count == 0, $"{novo.Social.Conhecidos.Count}");
		AfirmarRec("...e o Amigo nao o conhece antes de reencontra-lo", !a.Social.Conhece(sigB));
		Fotografar(a, novo, NowMs());
		Conhecido? doZero = a.Social.Ficha(sigB);
		AfirmarRec("**O AMIGO REENCONTRA O REENCARNADO DO ZERO**: convivio 0, nenhuma declaracao, nenhuma historia, conhecido desde AGORA",
				  doZero is { Familiaridade: 0, Relacao: Relacao.Nenhuma, Nome: "Novo", Historia: "" } && doZero.DesdeMs >= NowMs() - 5_000,
				  $"{doZero?.Nome}/{doZero?.Familiaridade}/{doZero?.Relacao}");

		// ---------------------------------------------------------------- D. o apagamento pela selecao
		GD.Print("[reciclado] -- D) a Vitima (a de agora) e apagada pela tela de selecao --");
		bool apagouSemDefeito = ApagarPelaSelecao(mesa, vitima, ContaRc("vitima"), "VitimaDois");
		bool abaSemAVitima = mesa.Bombeia(() => amigo.Conhecidos is { } k && k.All(c => c.Sig != sigV), 3000);
		AfirmarRec("**O APAGAMENTO PELA TELA DE SELECAO**: o Amigo esquece a Vitima (servidor e aba), e o Longe no disco",
				  apagouSemDefeito && !a.Social.Conhece(sigV) && abaSemAVitima
				  && ListaNoDisco(ContaRc("longe")) is { } l3 && !l3.Conhece(sigV),
				  $"apagou={apagouSemDefeito} servidor={a.Social.Conhece(sigV)} aba={abaSemAVitima}");

		// ---------------------------------------------------------------- C. a criacao limpa o resto de antes
		GD.Print("[reciclado] -- C) quem nasce numa vaga limpa as fichas que sobraram dela --");
		a.Social.Conhecidos[sigV] = new Conhecido { Assinatura = sigV, Nome = "VitimaDois", Familiaridade = 99, Relacao = Relacao.Amor };
		PlantarNoDisco(ContaRc("longe"), new Conhecido { Assinatura = sigV, Nome = "VitimaDois", Familiaridade = 99 });
		ServerPlayer? v3 = CriarNaVaga(mesa, vitima, ContaRc("vitima"), "VitimaTres");
		AfirmarRec("**A CRIACAO NA VAGA LIMPA O RESTO**: a ficha plantada da assinatura sai da lista do Amigo e do disco do Longe",
				  v3 != null && !a.Social.Conhece(sigV) && ListaNoDisco(ContaRc("longe")) is { } l4 && !l4.Conhece(sigV));

		// ---------------------------------------------------------------- B. a varredura do boot
		GD.Print("[reciclado] -- B) a varredura do boot, sobre saves plantados --");
		bool telaSaiu = mesa.SaiDoJogo(tela, ContaRc("tela"));   // fora da selecao: a copia viva dele nao regrava o disco depois
		long agora = NowMs();
		const long Dia = 86_400_000L;
		PlantarConta(ContaRc("fantasma"), new CharacterSave { Nome = "Fantasma", CriadoEm = agora - Dia }, null,
					 new CharacterSave { Nome = "Antigo", CriadoEm = 0 });
		PlantarConta(ContaRc("recente"), new CharacterSave { Nome = "Recente", CriadoEm = agora }, null, null);
		string sVazia = ServerPlayer.AssinaturaDe(ContaRc("fantasma"), 1);
		string sFantasma = ServerPlayer.AssinaturaDe(ContaRc("fantasma"), 0);
		string sAntigo = ServerPlayer.AssinaturaDe(ContaRc("fantasma"), 2);
		string sRecente = ServerPlayer.AssinaturaDe(ContaRc("recente"), 0);
		const string sDeNinguem = "0000000042";
		PlantarNoDisco(ContaRc("longe"),
			new Conhecido { Assinatura = sVazia, Nome = "Vazia" },
			new Conhecido { Assinatura = sFantasma, Nome = "Fantasma", DesdeMs = agora - 3 * Dia },
			new Conhecido { Assinatura = sRecente, Nome = "Recente", DesdeMs = 0 },
			new Conhecido { Assinatura = sAntigo, Nome = "Antigo", DesdeMs = 0 },
			new Conhecido { Assinatura = sDeNinguem, Nome = "DeNinguem", DesdeMs = agora - 3 * Dia });
		PlantarNoDisco(ContaRc("tela"), new Conhecido { Assinatura = sFantasma, Nome = "Fantasma", DesdeMs = agora - Dia / 24 });
		string[] tiradas = [sVazia, sFantasma, sRecente];
		string[] ficam = [sAntigo, sDeNinguem];
		AfirmarRec("(montagem) as seis fichas plantadas estao no disco do Longe e do Tela", telaSaiu
				  && ListaNoDisco(ContaRc("longe")) is { } p && tiradas.Concat(ficam).All(p.Conhece)
				  && ListaNoDisco(ContaRc("tela")) is { } pt && pt.Conhece(sFantasma));

		ConvivioVelhoFicaDeTeste = true;
		try
		{
			VarrerOConvivioDeQuemNaoExisteMais();
			AfirmarRec("(defeito injetado: a varredura do boot nao tira nada) as fichas de quem nao existe mais FICAM no disco do Longe -- a "
					  + "regua de baixo REPROVA", ListaNoDisco(ContaRc("longe")) is { } p2 && tiradas.All(p2.Conhece));
		}
		finally { ConvivioVelhoFicaDeTeste = false; }

		VarrerOConvivioDeQuemNaoExisteMais();
		Convivio? depois = ListaNoDisco(ContaRc("longe"));
		// UMA AFIRMACAO POR REGRA: a contraprova de cada uma derruba so a dela.
		AfirmarRec("**A VARREDURA DO BOOT** tira a ficha da VAGA VAZIA (o personagem apagado ou reencarnado que ainda nao foi recriado)",
				   depois != null && !depois.Conhece(sVazia));
		AfirmarRec("**A VARREDURA DO BOOT** tira a ficha de quem eu conhecia ANTES de ele nascer (a vaga ja tem outro personagem)",
				   depois != null && !depois.Conhece(sFantasma));
		AfirmarRec("**A VARREDURA DO BOOT** tira a ficha SEM DATA de quem nasceu depois que toda ficha passou a ter data",
				   depois != null && !depois.Conhece(sRecente));
		AfirmarRec("...e ficam a SEM DATA de quem nasceu antes do campo existir e a de assinatura que nenhuma vaga tem (na duvida, fica)",
				  depois != null && ficam.All(depois.Conhece));
		AfirmarRec("...e a do Tela, que conheceu o Fantasma DEPOIS de ele nascer, fica -- a mesma assinatura, a outra data",
				  ListaNoDisco(ContaRc("tela")) is { } dt2 && dt2.Conhece(sFantasma));
		AfirmarRec("...e o placar da varredura conta as tres", VarreduraDoConvivio is { Rodou: true, Fichas: 3 },
				  $"{VarreduraDoConvivio}");
	}

	/// <summary>Sai do mundo, volta ate a selecao e apaga o personagem da vaga 1 digitando o nome. Devolve se a vaga ficou vazia.</summary>
	private static bool ApagarPelaSelecao(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, string conta, string nome)
	{
		if (cli.NoMundo && !mesa.SaiDoJogo(cli, conta)) return false;
		if (!cli.Conectado && (!mesa.Entra(cli) || !mesa.Loga(cli, conta))) return false;
		cli.ApagarPersonagem(0, nome);
		return mesa.Bombeia(() => cli.Slots != null || cli.Recusa != null, 4000) && cli.Slots is { Count: > 0 } s && !s[0].Ocupado;
	}

	/// <summary>Da tela de selecao (ou de fora), cria um personagem na vaga 1 e entra no mundo com ele. Devolve o corpo, ou nulo.</summary>
	private static ServerPlayer? CriarNaVaga(MesaDeClientesCrus mesa, ClienteCruDeTeste cli, string conta, string nome)
	{
		if (!cli.Conectado && (!mesa.Entra(cli) || !mesa.Loga(cli, conta))) return null;
		cli.CriarPersonagem(0, FichaDeClienteCru(nome));
		return mesa.Bombeia(() => cli.NoMundo || cli.Recusa != null, 8000) && cli.NoMundo ? mesa.CorpoDe(conta) : null;
	}

	/// <summary>A lista do personagem da vaga 1 desta conta, lida DO DISCO agora (nulo = sem conta ou vaga vazia).</summary>
	private Convivio? ListaNoDisco(string conta) => _store?.Carregar(conta)?.Slots[0]?.Social;

	/// <summary>A lista do personagem da vaga 1 na copia VIVA da conta (a da tela de selecao ou a do mundo), ou nulo.</summary>
	private Convivio? ListaViva(string conta) => ContaViva(conta)?.Slots[0]?.Social;

	/// <summary>Planta fichas na lista da vaga 1 desta conta, no disco.</summary>
	private void PlantarNoDisco(string conta, params Conhecido[] fichas)
	{
		if (_store?.Carregar(conta) is not { } acc || acc.Slots[0] is not { } c) return;
		foreach (Conhecido f in fichas) c.Social.Conhecidos[f.Assinatura] = f;
		_store.Gravar(acc);
	}

	/// <summary>Uma conta de mentira gravada no disco, com estas tres vagas (nulo = vaga vazia).</summary>
	private void PlantarConta(string conta, CharacterSave? s0, CharacterSave? s1, CharacterSave? s2)
	{
		var acc = new AccountSave { Conta = conta };
		acc.Slots[0] = s0;
		acc.Slots[1] = s1;
		acc.Slots[2] = s2;
		_store?.Gravar(acc);
	}
}
