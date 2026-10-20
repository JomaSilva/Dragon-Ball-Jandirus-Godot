using System.Text.Json;
using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--servicosteste` (2026-10-19) -- OS SERVICOS DO OUTRO MUNDO, a rede de quests que paga zeni aos mortos (pedido de jogador
/// repassado pelo dono, que aprovou o desenho). A regra mora em `Core/World/ServicosDoAlem.cs`; o servidor, em `GameServer.ServicosDoAlem.cs`.
///
///     Godot --headless --server --port 9411 --servicosteste
///
/// Um cliente cru entra pela porta (os verbos do menu E sao do jogador de verdade, e o cartao vai pelo fio), e cada familia mede pela
/// porta de PRODUCAO -- os verbos `alem_pedir`/`alem_entregar`/`alem_desistir`, o funil `AoPerderALuta` e o tique de 1 Hz --, com o
/// defeito injetado que a mesma regua REPROVA:
///   D. OS DOADORES -- os tres novos semeados no lugar deles (o Oni e a Princesa no Outro Mundo, o Oni no Inferno), e o menu E dos quatro.
///   V. SO OS MORTOS -- o vivo e mandado embora. Contra-exemplo: `VivoPegaDeTeste`.
///   E. A ENTREGA E A REDE -- a lista do Oni, a entrega ao Sr. Kaioh que paga e apresenta, e o Sr. Kaioh que so atende depois dela.
///      Contra-exemplo: `RedeAbertaDeTeste`.
///   R. A RECARGA -- o servico feito volta so no dia seguinte do jogo. Contra-exemplo: `SemRecargaDeTeste`.
///   L. O LUGAR -- treinar no planeta do Sr. Kaioh e meditar no palacio da Princesa contam; fora deles, nao. Contra-exemplo:
///      `ForaDoLugarContaDeTeste`.
///   N. O NOCAUTE -- tres almas diferentes pagam; a mesma alma duas vezes conta uma; outro molde nao conta. Contra-exemplo:
///      `MesmoCorpoContaDeTeste`.
///   P. O PRAZO E O REVIVE -- o servico vencido cai, e o revivido o larga.
///   C. O CARTAO E O SAVE -- o `S2C.ServicoDoAlem` aberto com o leitor do cliente (o morto ve, o vivo nao; so sai quando muda) e a ficha
///      de servicos indo e voltando do JSON do `Fighter`.
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDosServicos = "bancada_servicos_ag";
	private const int IdBaseDosServicos = 99_500;

	private int _svOk, _svFalhou;
	private readonly List<ServerPlayer> _svForjados = [];

	private void AfirmarSv(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _svOk++; GD.Print($"[servicos]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _svFalhou++; GD.PrintErr($"[servicos]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDosServicos(int porta)
	{
		_svOk = _svFalhou = 0;
		GD.Print("[servicos] ================ OS SERVICOS DO OUTRO MUNDO (2026-10-19) ================");
		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		try
		{
			ServerPlayer? pl = mesa.EntraNoMundo(cli, ContaDaBancadaDosServicos, "Finado");
			AfirmarSv("(montagem) o cliente entra pela porta e e jogador; ha moldes", pl != null && EhJogador(pl) && _moldes != null);
			if (pl == null || _moldes == null) return;

			OsDoadoresSv();
			SoOsMortosSv(pl);
			MatarSv(pl);
			AEntregaEARedeSv(pl);
			ARecargaSv(pl);
			OLugarSv(pl);
			ONocauteSv(pl);
			OPrazoEORreviveSv(pl);
			OCartaoEOSaveSv(pl);
		}
		catch (Exception e) { AfirmarSv("a bancada rodou ate o fim", false, e.ToString()); }
		finally
		{
			ServicosDoAlem.VivoPegaDeTeste = false;
			ServicosDoAlem.RedeAbertaDeTeste = false;
			ServicosDoAlem.SemRecargaDeTeste = false;
			ServicosDoAlem.MesmoCorpoContaDeTeste = false;
			ServicosDoAlem.ForaDoLugarContaDeTeste = false;
			EscutaDeServicosDoAlem = null;
			EscutaDeEscolhas = null;
			foreach (ServerPlayer c in _svForjados) if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			_svForjados.Clear();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDosServicos + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[servicos] nao apaguei '{ContaDaBancadaDosServicos}': {e.Message}"); }
			GD.Print($"[servicos] ================ {_svOk} OK, {_svFalhou} FALHA(S) ================");
		}
	}

	// ---- as maos ----

	/// <summary>Poe o corpo do jogador na celula pedida da zona (pela porta de zona de producao).</summary>
	private void IrSv(ServerPlayer pl, string zona, int cx, int cy)
	{
		const float t = ZoneCollision.TileSize;
		MoveToZone(pl.Id, ZoneKey.Premade(zona), new Vec2(cx * t + t / 2f, cy * t + t / 2f));
	}

	/// <summary>Do lado do doador (uma celula ao sul dele: a obra e densa).</summary>
	private void AoLadoSv(ServerPlayer pl, string zona, int cx, int cy) => IrSv(pl, zona, cx, cy + 1);

	private List<string> VerboSv(ServerPlayer pl, string cmd, string arg = "") => Ouvir(() => Verbo(pl, cmd, arg));

	private static bool DisseSv(List<string> falas, string trecho) => falas.Any(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private void MatarSv(ServerPlayer pl)
	{
		pl.Ficha.dead = true;
		pl.MorteJaViajou = true;
	}

	private void ReviverSv(ServerPlayer pl) => pl.Ficha.dead = false;

	private FichaDoAlem FichaSv(ServerPlayer pl) => pl.Ficha.ServicosNoAlem ??= new FichaDoAlem();

	private void ZerarSv(ServerPlayer pl) => pl.Ficha.ServicosNoAlem = new FichaDoAlem();

	// =====================================================================
	// D) OS DOADORES
	// =====================================================================
	private void OsDoadoresSv()
	{
		GD.Print("[servicos] -- D) os doadores no lugar deles --");
		const float t = ZoneCollision.TileSize;
		(string Tipo, string Zona, int Cx, int Cy)[] doadores =
		[
			(ServicosDoAlem.TipoDaRecepcao, Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY),
			(ServicosDoAlem.TipoDaPrincesa, Alem.ZonaDoOutroMundo, ServicosDoAlem.PrincesaX, ServicosDoAlem.PrincesaY),
			(ServicosDoAlem.TipoDoOniDoInferno, Alem.ZonaDoInferno, ServicosDoAlem.OniDoInfernoX, ServicosDoAlem.OniDoInfernoY),
		];
		foreach ((string tipo, string zona, int cx, int cy) in doadores)
		{
			Obra? o = _noChao.FirstOrDefault(x => x.Tipo == tipo);
			AfirmarSv($"D. o doador `{tipo}` foi semeado em {zona} ({cx},{cy}), fixo e com a arte dele",
					  o != null && o.Zona.Equals(ZoneKey.Premade(zona)) && Math.Abs(o.X - (cx * t + t / 2f)) < 1 && Math.Abs(o.Y - (cy * t + t / 2f)) < 1
					  && o.DoMapa && _obras?.Get(tipo)?.Arte.StartsWith("res://Assets/Sprites/NPCs/", StringComparison.Ordinal) == true,
					  o == null ? "(nao ha)" : $"{o.Zona.Name} ({o.X},{o.Y})");
			// A CELULA E CHAO NO MAPA (sem a obra): o doador nao nasceu dentro de uma parede
			ZoneCollision? mapa = _catalogo?.Get(ZoneKey.Premade(zona))?.Mapa;
			AfirmarSv($"D. a celula do `{tipo}` e chao no mapa do lugar (e a do lado, de onde se fala com ele, tambem)",
					  mapa != null && !mapa.BlockedCell(cx, cy + 1));
		}
		string[] tipos = [ServicosDoAlem.TipoDaRecepcao, ServicosDoAlem.TipoDaPrincesa, ServicosDoAlem.TipoDoOniDoInferno, NpcsDeConversa.TipoDoSrKaioh];
		AfirmarSv("D. o menu E dos quatro doadores tem \"Pedir serviço\" (a lista) e \"Entregar\" -- e o Sr. Kaioh continua falando",
				  tipos.All(x => Interacoes.Aceita(x, ServicosDoAlem.VerboPedir) && Interacoes.Aceita(x, ServicosDoAlem.VerboEntregar))
				  && Interacoes.Aceita(NpcsDeConversa.TipoDoSrKaioh, "kaioh_falar"));
	}

	// =====================================================================
	// V) SO OS MORTOS
	// =====================================================================
	private void SoOsMortosSv(ServerPlayer pl)
	{
		GD.Print("[servicos] -- V) so os mortos --");
		ReviverSv(pl);
		ZerarSv(pl);
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY);
		List<string> falas = VerboSv(pl, ServicosDoAlem.VerboPedir, "Entrega ao Sr. Kaioh");
		AfirmarSv("V. **SO AS ALMAS TRABALHAM**: o vivo pede o servico e o Oni o manda embora",
				  FichaSv(pl).Atual.Length == 0 && DisseSv(falas, "só as almas"), string.Join(" | ", falas));
		ServicosDoAlem.VivoPegaDeTeste = true;
		try { VerboSv(pl, ServicosDoAlem.VerboPedir, "Entrega ao Sr. Kaioh"); }
		finally { ServicosDoAlem.VivoPegaDeTeste = false; }
		AfirmarSv("(defeito injetado: o vivo tambem pega) a mesma regua REPROVA -- o vivo sai com o servico", FichaSv(pl).Atual.Length > 0);
		ZerarSv(pl);
	}

	// =====================================================================
	// E) A ENTREGA E A REDE
	// =====================================================================
	private void AEntregaEARedeSv(ServerPlayer pl)
	{
		GD.Print("[servicos] -- E) a entrega e a rede --");
		ZerarSv(pl);
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY);
		EscutaDeEscolhas = [];
		List<string> lista = VerboSv(pl, ServicosDoAlem.VerboPedir);
		List<string> itens = EscutaDeEscolhas.LastOrDefault(e => e.Quem == pl.Id).Itens ?? [];
		EscutaDeEscolhas = null;
		AfirmarSv("E. o Oni da recepcao lista os quatro servicos dele, com o pagamento, e a lista sobe pro menu",
				  itens.SequenceEqual(ServicosDoAlem.De(DoadorDoAlem.Recepcao).Select(s => s.Nome)) && DisseSv(lista, "40.000 zeni"),
				  string.Join(", ", itens));

		// O Sr. Kaioh ainda nao conhece o morto
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, NpcsDeConversa.SrKaiohX, NpcsDeConversa.SrKaiohY);
		List<string> antes = VerboSv(pl, ServicosDoAlem.VerboPedir, "Treino na gravidade");
		AfirmarSv("E. **A REDE**: o Sr. Kaioh nao da servico a quem nunca lhe fez uma entrega",
				  FichaSv(pl).Atual.Length == 0 && DisseSv(antes, "ainda não te conhece"), string.Join(" | ", antes));
		ServicosDoAlem.RedeAbertaDeTeste = true;
		try { VerboSv(pl, ServicosDoAlem.VerboPedir, "Treino na gravidade"); }
		finally { ServicosDoAlem.RedeAbertaDeTeste = false; }
		AfirmarSv("(defeito injetado: a rede aberta) a mesma regua REPROVA -- o Sr. Kaioh da o treino a quem ele nao conhece",
				  FichaSv(pl).Atual == "treino_kaioh");
		ZerarSv(pl);

		// a entrega: pega na recepcao, entrega no planeta do Sr. Kaioh
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY);
		VerboSv(pl, ServicosDoAlem.VerboPedir, "Entrega ao Sr. Kaioh");
		AfirmarSv("E. o morto pega a entrega ao Sr. Kaioh na recepcao", FichaSv(pl).Atual == "entrega_kaioh", FichaSv(pl).Atual);
		List<string> errado = VerboSv(pl, ServicosDoAlem.VerboEntregar);
		AfirmarSv("E. entregar pro doador errado nao cumpre nada (a recepcao nao e o destino)",
				  FichaSv(pl).Atual == "entrega_kaioh" && DisseSv(errado, "não espera nada"), string.Join(" | ", errado));
		double zeni = pl.Ficha.Zeni;
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, NpcsDeConversa.SrKaiohX, NpcsDeConversa.SrKaiohY);
		List<string> entregue = VerboSv(pl, ServicosDoAlem.VerboEntregar);
		AfirmarSv("E. **A ENTREGA PAGA**: o Sr. Kaioh recebe, o servico acaba, e 40.000 zeni entram no bolso",
				  FichaSv(pl).Atual.Length == 0 && Math.Abs(pl.Ficha.Zeni - zeni - 40_000) < 0.5 && DisseSv(entregue, "+40.000 zeni"),
				  $"{pl.Ficha.Zeni - zeni:N0} | {string.Join(" | ", entregue)}");
		VerboSv(pl, ServicosDoAlem.VerboPedir, "Treino na gravidade");
		AfirmarSv("E. **...E APRESENTA**: agora o Sr. Kaioh da o treino na gravidade", FichaSv(pl).Atual == "treino_kaioh", FichaSv(pl).Atual);
		VerboSv(pl, ServicosDoAlem.VerboDesistir);
		AfirmarSv("E. desistir larga o servico (pelo verbo do botao da aba World)", FichaSv(pl).Atual.Length == 0);
	}

	// =====================================================================
	// R) A RECARGA
	// =====================================================================
	private void ARecargaSv(ServerPlayer pl)
	{
		GD.Print("[servicos] -- R) a recarga de um dia do jogo --");
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY);
		List<string> falas = VerboSv(pl, ServicosDoAlem.VerboPedir, "Entrega ao Sr. Kaioh");
		AfirmarSv("R. **O SERVICO FEITO SO VOLTA NO DIA SEGUINTE DO JOGO**: a entrega de agora ha pouco nao e dada de novo",
				  FichaSv(pl).Atual.Length == 0 && DisseSv(falas, "já foi feito hoje"), string.Join(" | ", falas));
		ServicosDoAlem.SemRecargaDeTeste = true;
		try { VerboSv(pl, ServicosDoAlem.VerboPedir, "Entrega ao Sr. Kaioh"); }
		finally { ServicosDoAlem.SemRecargaDeTeste = false; }
		AfirmarSv("(defeito injetado: sem recarga) a mesma regua REPROVA -- a mesma entrega sai de novo na hora", FichaSv(pl).Atual == "entrega_kaioh");
		VerboSv(pl, ServicosDoAlem.VerboDesistir);
		FichaSv(pl).DeNovoEm["entrega_kaioh"] = NowMs() - 1;
		VerboSv(pl, ServicosDoAlem.VerboPedir, "Entrega ao Sr. Kaioh");
		AfirmarSv("R. passado o dia, o servico volta", FichaSv(pl).Atual == "entrega_kaioh");
		VerboSv(pl, ServicosDoAlem.VerboDesistir);
	}

	// =====================================================================
	// L) O LUGAR
	// =====================================================================
	private void OLugarSv(ServerPlayer pl)
	{
		GD.Print("[servicos] -- L) o lugar do treino e da meditacao --");
		// O TREINO: so no planeta do Sr. Kaioh
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, NpcsDeConversa.SrKaiohX, NpcsDeConversa.SrKaiohY);
		VerboSv(pl, ServicosDoAlem.VerboPedir, "Treino na gravidade");
		pl.Ficha.train = true;
		IrSv(pl, Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX - 6, ServicosDoAlem.RecepcaoY);   // longe da ilha: no palacio do Enma
		for (int i = 0; i < 10; i++) TickDosServicosDoAlem();
		AfirmarSv("L. treinar FORA do planeta do Sr. Kaioh nao conta", FichaSv(pl).Progresso == 0, $"{FichaSv(pl).Progresso}");
		ServicosDoAlem.ForaDoLugarContaDeTeste = true;
		try { for (int i = 0; i < 10; i++) TickDosServicosDoAlem(); }
		finally { ServicosDoAlem.ForaDoLugarContaDeTeste = false; }
		AfirmarSv("(defeito injetado: fora do lugar conta) a mesma regua REPROVA -- o treino no palacio do Enma soma", FichaSv(pl).Progresso > 0);
		FichaSv(pl).Progresso = 0;

		double zeni = pl.Ficha.Zeni;
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, NpcsDeConversa.SrKaiohX, NpcsDeConversa.SrKaiohY);
		for (int i = 0; i < 299; i++) TickDosServicosDoAlem();
		AfirmarSv("L. no planeta do Sr. Kaioh cada segundo de treino conta (299 de 300)", FichaSv(pl).Progresso == 299 && FichaSv(pl).Atual == "treino_kaioh",
				  $"{FichaSv(pl).Progresso}");
		List<string> pago = Ouvir(TickDosServicosDoAlem);
		AfirmarSv("L. **5 MINUTOS DE TREINO NA GRAVIDADE PAGAM 50.000**", FichaSv(pl).Atual.Length == 0 && Math.Abs(pl.Ficha.Zeni - zeni - 50_000) < 0.5,
				  $"{pl.Ficha.Zeni - zeni:N0} | {string.Join(" | ", pago)}");
		pl.Ficha.train = false;

		// A MEDITACAO: so no palacio da Princesa
		zeni = pl.Ficha.Zeni;
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, ServicosDoAlem.PrincesaX, ServicosDoAlem.PrincesaY);
		VerboSv(pl, ServicosDoAlem.VerboPedir, "Chá da Princesa");
		pl.Ficha.med = true;
		for (int i = 0; i < 180; i++) TickDosServicosDoAlem();
		AfirmarSv("L. **3 MINUTOS MEDITANDO NO PALACIO DA PRINCESA PAGAM 20.000**",
				  FichaSv(pl).Atual.Length == 0 && Math.Abs(pl.Ficha.Zeni - zeni - 20_000) < 0.5, $"{pl.Ficha.Zeni - zeni:N0}");
		pl.Ficha.med = false;
	}

	// =====================================================================
	// N) O NOCAUTE
	// =====================================================================
	private ServerPlayer AlmaSv(int i, string molde)
	{
		MoldeDeNpc m = _moldes!.Get(molde)!;
		var a = new ServerPlayer
		{
			Id = IdBaseDosServicos + i, Peer = null, Name = $"bancada: {molde} {i}", Race = "Human", Genero = "Male", Idade = 30,
			Zone = ZoneKey.Premade(Alem.ZonaDoOutroMundo), Pos = new Vec2(0, 0), Conta = "", Slot = -1,
			Ficha = new Fighter { Race = "Human", BP = 100 }, Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		a.Papel = new PapelDeNpc(m, (ulong)i);
		PorNoMundo(a);
		_svForjados.Add(a);
		return a;
	}

	private void ONocauteSv(ServerPlayer pl)
	{
		GD.Print("[servicos] -- N) o nocaute --");
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY);
		VerboSv(pl, ServicosDoAlem.VerboPedir, "Almas briguentas");
		ServerPlayer a1 = AlmaSv(1, "alma"), a2 = AlmaSv(2, "alma"), a3 = AlmaSv(3, "alma"), condenada = AlmaSv(4, "alma_condenada");
		AoPerderALuta(a1, pl, false);
		AoPerderALuta(a1, pl, false);
		AfirmarSv("N. **A MESMA ALMA DUAS VEZES CONTA UMA**", FichaSv(pl).Progresso == 1, $"{FichaSv(pl).Progresso}");
		ServicosDoAlem.MesmoCorpoContaDeTeste = true;
		try { AoPerderALuta(a1, pl, false); }
		finally { ServicosDoAlem.MesmoCorpoContaDeTeste = false; }
		AfirmarSv("(defeito injetado: o mesmo corpo conta de novo) a mesma regua REPROVA -- a alma repetida soma", FichaSv(pl).Progresso == 2);
		FichaSv(pl).Progresso = 1;
		AoPerderALuta(condenada, pl, false);
		AfirmarSv("N. a alma condenada nao conta nas almas briguentas (outro molde)", FichaSv(pl).Progresso == 1, $"{FichaSv(pl).Progresso}");
		double zeni = pl.Ficha.Zeni;
		AoPerderALuta(a2, pl, false);
		AoPerderALuta(a3, pl, true);
		AfirmarSv("N. **TRES ALMAS DIFERENTES PAGAM 30.000** (a derrubada e a morta contam pelo mesmo funil)",
				  FichaSv(pl).Atual.Length == 0 && Math.Abs(pl.Ficha.Zeni - zeni - 30_000) < 0.5, $"{pl.Ficha.Zeni - zeni:N0}");
	}

	// =====================================================================
	// P) O PRAZO E O REVIVE
	// =====================================================================
	private void OPrazoEORreviveSv(ServerPlayer pl)
	{
		GD.Print("[servicos] -- P) o prazo e o revive --");
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY);
		VerboSv(pl, ServicosDoAlem.VerboPedir, "Convite à Princesa");
		FichaSv(pl).PrazoMs = NowMs() - 1;
		List<string> falas = Ouvir(TickDosServicosDoAlem);
		AfirmarSv("P. **O PRAZO VENCIDO DERRUBA O SERVICO**, avisado", FichaSv(pl).Atual.Length == 0 && DisseSv(falas, "venceu"), string.Join(" | ", falas));
		VerboSv(pl, ServicosDoAlem.VerboPedir, "Convite à Princesa");
		ReviverSv(pl);
		falas = Ouvir(TickDosServicosDoAlem);
		AfirmarSv("P. **O REVIVIDO LARGA O SERVICO**: o vivo nao trabalha pro Outro Mundo",
				  FichaSv(pl).Atual.Length == 0 && DisseSv(falas, "voltou à vida"), string.Join(" | ", falas));
		MatarSv(pl);
	}

	// =====================================================================
	// C) O CARTAO E O SAVE
	// =====================================================================
	private void OCartaoEOSaveSv(ServerPlayer pl)
	{
		GD.Print("[servicos] -- C) o cartao e o save --");
		AoLadoSv(pl, Alem.ZonaDoOutroMundo, ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY);
		VerboSv(pl, ServicosDoAlem.VerboPedir, "Recado do Inferno");
		EscutaDeServicosDoAlem = [];
		_sigServicosDoAlem.Remove(pl.Id);
		TickDosServicosDoAlem();
		TickDosServicosDoAlem();
		List<NetDataWriter> pacotes = [.. EscutaDeServicosDoAlem.Where(e => e.Quem == pl.Id).Select(e => e.Pacote)];
		ServicoDoAlemState? lido = pacotes.Count > 0 ? LerCartaoSv(pacotes[0]) : null;
		AfirmarSv("C. **O CARTAO DO MORTO PELO FIO**: o servico em curso, o andamento, o pagamento e a rede dos sete -- lido com o leitor do cliente",
				  lido is { Mostrar: true, Titulo: "Recado do Inferno" } s && s.Andamento.Contains("Oni do Inferno") && s.Recompensa == 30_000
				  && s.Rede.Count == ServicosDoAlem.Todos.Length && s.Rede.Any(l => l.Estado == "em curso"),
				  lido is { } x ? $"{x.Titulo} | {x.Andamento} | {x.Rede.Count}" : "(nada)");
		AfirmarSv("C. o cartao so sai quando muda (dois tiques, um pacote)", pacotes.Count == 1, $"{pacotes.Count}");

		// O SAVE: a ficha de servicos vai e volta do JSON do `Fighter` (o `CharacterSave.Ficha`, `IncludeFields`)
		var opcoes = new JsonSerializerOptions { IncludeFields = true };
		Fighter? volta = JsonSerializer.Deserialize<Fighter>(JsonSerializer.Serialize(pl.Ficha, opcoes), opcoes);
		AfirmarSv("C. a ficha de servicos atravessa o save (o servico em curso, as recargas e quem ja te conhece)",
				  volta?.ServicosNoAlem is { Atual: "recado_inferno" } f2 && f2.DeNovoEm.ContainsKey("entrega_kaioh")
				  && f2.Apresentado.Contains(DoadorDoAlem.SrKaioh));
		VerboSv(pl, ServicosDoAlem.VerboDesistir);

		ReviverSv(pl);
		EscutaDeServicosDoAlem = [];
		TickDosServicosDoAlem();
		// NULO SEM PACOTE: o `FirstOrDefault` de um struct devolveria o cartao padrao, com `Mostrar` falso -- e a regua passaria sem ter lido
		// nada (a r1 de 2026-10-19: o vivo atendido do mutante x1 nao manda pacote nenhum na volta a vida).
		ServicoDoAlemState? doVivo = EscutaDeServicosDoAlem.Where(e => e.Quem == pl.Id).Select(e => (ServicoDoAlemState?)LerCartaoSv(e.Pacote))
			.FirstOrDefault();
		AfirmarSv("C. o vivo nao ve o cartao (o pacote diz pra esconder)", doVivo is { Mostrar: false }, doVivo is { } v ? $"{v.Mostrar}" : "(nenhum pacote)");
		EscutaDeServicosDoAlem = null;
	}

	/// <summary>Abre um pacote do cartao com o leitor do CLIENTE (`FioDoServicoDoAlem.Ler`), depois do byte do opcode.</summary>
	private static ServicoDoAlemState LerCartaoSv(NetDataWriter w)
	{
		var r = new NetDataReader(w.CopyData());
		r.GetByte();
		return FioDoServicoDoAlem.Ler(r);
	}
}
