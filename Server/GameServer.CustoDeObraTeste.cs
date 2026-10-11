using System.Text.Json.Nodes;
using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A FAMILIA 11 DA `--blocoteste`: O CUSTO DA OBRA -- o pedido do dono de 2026-10-10 (*"construir paredes etc
/// tem custo em zenni/ recursos o usuario escolhe na hora de construir e essa opcao fica selecionada ate ele
/// quiser trocar"*), pelo funil de producao: o pacote de erguer (`ComandoDeBloco`) e o verbo da moeda (`Verbo`).
///
/// O que ela cobra, cada linha com o seu contra-exemplo:
///   * a peca sai pelo preco da CLASSE, em zeni;                      (defeito: a obra de graca)
///   * em recursos sai o DOBRO;                                       (defeito: recurso sem o dobro)
///   * sem saldo nao sobe nem cobra, e a outra moeda nao completa;    (defeitos: obra fiada / a outra completa)
///   * a escolha FICA ate ser trocada;                                (defeito: vale so pra proxima peca)
///   * em lote, para na primeira que o bolso nao paga;                (defeito: obra fiada)
///   * uma resposta de saldo por pedido, tenha subido ou nao;         (defeito: so responde quando sobe)
///   * o mesmo pedido duas vezes nao cobra duas;                      (defeito: a mesma peca cobra de novo)
///   * desmanchar nao devolve;                                        (a constante do reembolso, ligada)
///   * a base de antes do preco volta de pe e nao cobra de ninguem.   (o contraste: a mesma parede, pelo funil, cobra)
///
/// A metade do CLIENTE (o seletor no canto, o fantasma vermelho, a escolha gravada) e do robo `--diagconstruir`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Os campos de um `S2C.SaldoDeObra`, lidos do fio na ordem do escritor. Zeni negativo = nao era esse pacote.</summary>
	private static (double Zeni, double Recursos, Moeda Moeda, bool DeUmaPeca, int Bytes) SaldoNoFio(byte[] fio)
	{
		var r = new NetDataReader(fio);
		if (r.GetByte() != (byte)Protocol.S2C.SaldoDeObra) return (-1, -1, Moeda.Zeni, false, fio.Length);
		return (r.GetDouble(), r.GetDouble(), (Moeda)r.GetByte(), r.GetBool(), fio.Length);
	}

	private void OCustoDaObra(ZoneKey terra, ZoneCollision mapa, int px, int py, Func<int, int, Vec2> no)
	{
		GD.Print("[bloco] -- 11) o custo: cada peca sai do bolso, na moeda que o jogador escolheu --");
		double reembolsoDeAntes = PrecoDeBloco.DevolveAoDesmanchar;
		string tmp = System.IO.Path.Combine(OS.GetUserDataDir(), "bases-de-antes-do-preco.json");

		try
		{
			ZerarBases();
			BlocoDef parede = Bloco(ClasseDeBloco.Parede)!, piso = Bloco(ClasseDeBloco.Piso)!, porta = Bloco(ClasseDeBloco.Porta)!;
			double pParede = PrecoDeBloco.Parede, pPiso = PrecoDeBloco.Piso, pPorta = PrecoDeBloco.Porta;
			double rParede = Recurso.CustoEmRecursos(pParede), rPiso = Recurso.CustoEmRecursos(pPiso);

			// O PAGADOR, no meio da quarta fileira da praca: as tres de cima (nove celulas cada, onde a familia
			// ergue) ficam todas ao alcance dele. E um ADMIN ao lado, que alcanca as celulas que usa.
			ServerPlayer pagador = Forjar("bl: o pagador", no(px + 4, py + 3), 1_000);
			pagador.Conta = "bancada_pagador";
			ServerPlayer adm = Forjar("bl: o admin", no(px + 3, py + 3), 1_000);
			adm.Conta = "bancada_admin";
			adm.Poderes |= Protocol.Poder.Admin;
			Fighter f = pagador.Ficha;

			int proxima = 0;
			(int X, int Y) Nova() { int i = proxima++; return (px + i % 9, py + i / 9); }
			bool DePe((int X, int Y) c) => BlocoEm(terra, c.X, c.Y) != null;
			void Erguer((int X, int Y) c, BlocoDef d) => ComandoDeBloco(pagador, Protocol.BlocoErguer, c.X, c.Y, d.Numero, "");
			void Tirar((int X, int Y) c) => ComandoDeBloco(pagador, Protocol.BlocoDesmanchar, c.X, c.Y, 0, "");
			void PagarCom(string chave) => Verbo(pagador, PrecoDeBloco.Verbo, chave);
			void Bolso(double zeni, double recursos) { f.Zeni = zeni; f.Recursos = recursos; }
			bool Tem(double zeni, double recursos) => Math.Abs(f.Zeni - zeni) < 1e-6 && Math.Abs(f.Recursos - recursos) < 1e-6;
			string Bolsos() => $"zeni {f.Zeni:0.##}, recursos {f.Recursos:0.##}";
			List<string> Ouvir(Action gesto)
			{
				List<string>? antes = EscutaDeAvisos;
				EscutaDeAvisos = [];
				try { gesto(); return EscutaDeAvisos; }
				finally { EscutaDeAvisos = antes; }
			}
			List<(int Para, byte[] Fio)> NoFio(Action gesto)
			{
				EscutaDeSaldoDeObra = [];
				try { gesto(); return EscutaDeSaldoDeObra; }
				finally { EscutaDeSaldoDeObra = null; }
			}

			// ------------------------------------------------------------ a tabela
			ClasseDeBloco[] classes = [ClasseDeBloco.Parede, ClasseDeBloco.Piso, ClasseDeBloco.Porta];
			AfirmarBl("(montagem) a tabela: as tres classes tem preco em zeni, e em recursos cada uma sai pelo `Recurso.CustoEmRecursos` (o dobro)",
					  classes.All(c => PrecoDeBloco.EmZeni(c) > 0
									   && Math.Abs(PrecoDeBloco.Na(c, Moeda.Recursos) - Recurso.CustoEmRecursos(PrecoDeBloco.EmZeni(c))) < 1e-9
									   && Math.Abs(PrecoDeBloco.Na(c, Moeda.Zeni) - PrecoDeBloco.EmZeni(c)) < 1e-9)
					  && rParede > pParede,
					  string.Join(", ", classes.Select(c => $"{PrecoDeBloco.NomeDaClasse(c)} {PrecoDeBloco.Etiqueta(c, Moeda.Zeni)} / {PrecoDeBloco.Etiqueta(c, Moeda.Recursos)}")));

			// ------------------------------------------------------------ em zeni, pelo preco da classe
			Bolso(1000, 1000);
			(int X, int Y) a = Nova(), b = Nova(), c = Nova(), d = Nova();
			Erguer(a, parede);
			AfirmarBl("quem nunca escolheu paga em ZENI: a parede sobe e sai do bolso o preco da PAREDE -- os recursos nao se mexem",
					  MoedaDeObra(pagador) == Moeda.Zeni && DePe(a) && Tem(1000 - pParede, 1000), Bolsos());
			AfirmarBl("...e o bloco anota o que custou, e de qual bolso",
					  BlocoEm(terra, a.X, a.Y) is { } ba && Math.Abs(ba.Pago - pParede) < 1e-9 && ba.PagoEm == (byte)Moeda.Zeni);

			ObraDeGracaDeTeste = true;
			try
			{
				Erguer(b, parede);
				AfirmarBl("(defeito injetado: a obra nao cobra) a parede sobe e o zeni NAO sai -- a obra de graca de antes do pedido",
						  DePe(b) && Tem(1000 - pParede, 1000), Bolsos());
			}
			finally { ObraDeGracaDeTeste = false; }
			Tirar(b);

			Erguer(c, piso);
			Erguer(d, porta);
			AfirmarBl("o piso sai pelo preco do PISO e a porta pelo da PORTA",
					  DePe(c) && DePe(d) && Tem(1000 - pParede - pPiso - pPorta, 1000), Bolsos());

			// ------------------------------------------------------------ em recursos, o dobro
			List<(int Para, byte[] Fio)> respostaDoVerbo = NoFio(() => PagarCom("recursos"));
			(double Zeni, double Recursos, Moeda Moeda, bool DeUmaPeca, int Bytes) rv =
				respostaDoVerbo.Count == 1 ? SaldoNoFio(respostaDoVerbo[0].Fio) : default;
			AfirmarBl("o verbo `obra_moeda recursos` troca a moeda e responde com os dois saldos e a moeda em uso (19 bytes, e nao e resposta de peca)",
					  MoedaDeObra(pagador) == Moeda.Recursos && respostaDoVerbo.Count == 1 && respostaDoVerbo[0].Para == pagador.Id
					  && rv is { Moeda: Moeda.Recursos, DeUmaPeca: false, Bytes: 19 } && Math.Abs(rv.Zeni - f.Zeni) < 1e-6 && Math.Abs(rv.Recursos - f.Recursos) < 1e-6,
					  $"{respostaDoVerbo.Count} resposta(s): zeni {rv.Zeni:0.##}, recursos {rv.Recursos:0.##}, moeda {rv.Moeda}, {rv.Bytes} bytes");

			double z = f.Zeni;
			(int X, int Y) e = Nova(), e2 = Nova();
			Erguer(e, parede);
			AfirmarBl("em RECURSOS a parede sai pelo DOBRO do preco em zeni -- e o zeni nao se mexe",
					  DePe(e) && Tem(z, 1000 - rParede)
					  && BlocoEm(terra, e.X, e.Y) is { } be && be.PagoEm == (byte)Moeda.Recursos && Math.Abs(be.Pago - rParede) < 1e-9,
					  Bolsos());

			RecursoSemDobroDeTeste = true;
			try
			{
				Erguer(e2, parede);
				AfirmarBl("(defeito injetado: recurso sem o dobro) a parede sai pelo MESMO numero do zeni",
						  DePe(e2) && Tem(z, 1000 - rParede - pParede), Bolsos());
			}
			finally { RecursoSemDobroDeTeste = false; }
			Tirar(e2);

			// ------------------------------------------------------------ sem saldo
			// Falta UM recurso pra parede, e ha dez paredes de zeni no outro bolso.
			Bolso(10 * pParede, rParede - 1);
			(int X, int Y) g = Nova();
			List<string> falas = Ouvir(() => Erguer(g, parede));
			AfirmarBl("SEM SALDO na moeda escolhida a parede NAO sobe, nada sai de bolso nenhum (o zeni NAO completa) e o jogador ouve por que",
					  !DePe(g) && Tem(10 * pParede, rParede - 1) && falas.Any(s => s.Contains("faltam recursos", StringComparison.Ordinal)),
					  $"{Bolsos()} | {string.Join(" | ", falas)}");

			ObraFiadaDeTeste = true;
			try
			{
				Erguer(g, parede);
				AfirmarBl("(defeito injetado: obra fiada) sem saldo a parede sobe do mesmo jeito", DePe(g) && Tem(10 * pParede, rParede - 1), Bolsos());
			}
			finally { ObraFiadaDeTeste = false; }
			Tirar(g);

			ObraCompletaComAOutraDeTeste = true;
			try
			{
				Erguer(g, parede);
				AfirmarBl("(defeito injetado: a outra moeda completa) a parede sobe e quem paga e o ZENI, que o jogador nao escolheu",
						  DePe(g) && Tem(9 * pParede, rParede - 1), Bolsos());
			}
			finally { ObraCompletaComAOutraDeTeste = false; }
			Tirar(g);

			// ------------------------------------------------------------ a escolha fica
			Bolso(1000, 1000);
			(int X, int Y) h1 = Nova(), h2 = Nova(), h3 = Nova();
			Erguer(h1, piso);
			Erguer(h2, parede);
			AfirmarBl("A ESCOLHA FICA: depois da recusa e de mais duas pecas, tudo continua saindo dos recursos",
					  MoedaDeObra(pagador) == Moeda.Recursos && DePe(h1) && DePe(h2) && Tem(1000, 1000 - rPiso - rParede), Bolsos());
			PagarCom("ouro");
			AfirmarBl("um argumento que nao e moeda nenhuma nao troca nada", MoedaDeObra(pagador) == Moeda.Recursos);

			MoedaDeObraNaoFicaDeTeste = true;
			try
			{
				Bolso(1000, 1000);
				(int X, int Y) k1 = Nova(), k2 = Nova();
				Erguer(k1, parede);
				Erguer(k2, parede);
				AfirmarBl("(defeito injetado: a escolha vale so pra proxima peca) a segunda parede ja sai do ZENI",
						  Tem(1000 - pParede, 1000 - rParede), Bolsos());
				Tirar(k1);
				Tirar(k2);
			}
			finally { MoedaDeObraNaoFicaDeTeste = false; }

			PagarCom("zeni");
			Bolso(1000, 1000);
			Erguer(h3, parede);
			AfirmarBl("trocada de volta pro zeni, a proxima peca sai do zeni",
					  MoedaDeObra(pagador) == Moeda.Zeni && DePe(h3) && Tem(1000 - pParede, 1000), Bolsos());

			// ------------------------------------------------------------ em lote
			// Cinco pedidos seguidos (o arrasto), com bolso pra TRES paredes e um troco que nao paga a quarta.
			double troco = pParede / 2;
			(int X, int Y)[] lote = [Nova(), Nova(), Nova(), Nova(), Nova()];
			Bolso(3 * pParede + troco, 1000);
			List<(int Para, byte[] Fio)> respostas = NoFio(() => { foreach ((int X, int Y) cel in lote) Erguer(cel, parede); });
			AfirmarBl("EM LOTE (cinco pedidos, bolso pra tres): sobem as tres primeiras, a quarta e a quinta nao, e sobra o troco",
					  DePe(lote[0]) && DePe(lote[1]) && DePe(lote[2]) && !DePe(lote[3]) && !DePe(lote[4]) && Tem(troco, 1000),
					  $"{lote.Count(DePe)} de pe | {Bolsos()}");
			(double Zeni, double Recursos, Moeda Moeda, bool DeUmaPeca, int Bytes) ultima = respostas.Count > 0 ? SaldoNoFio(respostas[^1].Fio) : default;
			AfirmarBl("...e o servidor responde UMA vez por pedido, tenha a peca subido ou nao: cinco respostas de peca, a ultima com o troco",
					  respostas.Count == 5 && respostas.All(x => x.Para == pagador.Id && SaldoNoFio(x.Fio).DeUmaPeca)
					  && Math.Abs(ultima.Zeni - troco) < 1e-6 && ultima.Moeda == Moeda.Zeni,
					  $"{respostas.Count} resposta(s), a ultima com zeni {ultima.Zeni:0.##}");
			foreach ((int X, int Y) cel in lote) if (DePe(cel)) Tirar(cel);

			ObraFiadaDeTeste = true;
			try
			{
				Bolso(3 * pParede + troco, 1000);
				foreach ((int X, int Y) cel in lote) Erguer(cel, parede);
				AfirmarBl("(defeito injetado: obra fiada) as cinco sobem, com bolso pra tres", lote.All(DePe), $"{lote.Count(DePe)} de pe");
			}
			finally { ObraFiadaDeTeste = false; }
			foreach ((int X, int Y) cel in lote) if (DePe(cel)) Tirar(cel);

			SaldoSoQuandoSobeDeTeste = true;
			try
			{
				Bolso(3 * pParede + troco, 1000);
				int so = NoFio(() => { foreach ((int X, int Y) cel in lote) Erguer(cel, parede); }).Count;
				AfirmarBl("(defeito injetado: o saldo so e respondido quando a peca sobe) cinco pedidos, tres respostas -- duas pecas ficariam 'a caminho' no cliente pra sempre",
						  so == 3, $"{so} resposta(s)");
			}
			finally { SaldoSoQuandoSobeDeTeste = false; }

			// ------------------------------------------------------------ o mesmo pedido duas vezes
			Bolso(1000, 1000);
			(int X, int Y) m = Nova();
			Erguer(m, parede);
			Erguer(m, parede);
			AfirmarBl("o MESMO pedido duas vezes (o arrasto que volta por cima antes do anuncio chegar): uma parede, paga UMA vez",
					  DePe(m) && Tem(1000 - pParede, 1000), Bolsos());
			MesmaPecaCobraDeNovoDeTeste = true;
			try
			{
				Erguer(m, parede);
				AfirmarBl("(defeito injetado: a mesma peca cobra de novo) a parede que ja estava la sai do bolso outra vez",
						  Tem(1000 - 2 * pParede, 1000), Bolsos());
			}
			finally { MesmaPecaCobraDeNovoDeTeste = false; }

			Bolso(1000, 1000);
			Erguer(m, piso);
			AfirmarBl("TROCAR a propria parede por um piso paga o piso inteiro, e a parede nao volta pro bolso",
					  BlocoEm(terra, m.X, m.Y)?.Def.Classe == ClasseDeBloco.Piso && Tem(1000 - pPiso, 1000), Bolsos());

			// ------------------------------------------------------------ so se paga pelo que sobe
			Bolso(1000, 1000);
			(int X, int Y) longe = (px + 4 + RegrasDeBloco.Alcance + 1, py + 3);
			Erguer(longe, parede);
			AfirmarBl("o que NAO sobe por causa do lugar (longe demais) nao custa nada: a cobranca e a ultima pergunta",
					  !DePe(longe) && Tem(1000, 1000), Bolsos());

			// ------------------------------------------------------------ o admin
			adm.Ficha.Zeni = 1000;
			(int X, int Y) q = Nova();
			ComandoDeBloco(adm, Protocol.BlocoErguer, q.X, q.Y, parede.Numero, "");
			AfirmarBl("o ADMIN paga como todo mundo -- nao ha modo de admin que constroi livre",
					  EhAdmin(adm) && DePe(q) && Math.Abs(adm.Ficha.Zeni - (1000 - pParede)) < 1e-6, $"zeni do admin {adm.Ficha.Zeni:0.##}");

			// ------------------------------------------------------------ desmanchar nao devolve
			Bolso(1000, 1000);
			(int X, int Y) n1 = Nova(), n2 = Nova(), n3 = Nova();
			Erguer(n1, parede);          // paga em zeni
			Erguer(n3, parede);          // paga em zeni; quem vai desmanchar e o admin
			PagarCom("recursos");
			Erguer(n2, parede);          // paga em recursos
			PagarCom("zeni");
			double zPago = f.Zeni, rPago = f.Recursos;
			Tirar(n1);
			AfirmarBl("DESMANCHAR NAO DEVOLVE: a parede sai e os dois bolsos ficam como estavam depois de pagar",
					  !DePe(n1) && Tem(zPago, rPago), Bolsos());

			PrecoDeBloco.DevolveAoDesmanchar = 0.5;
			try
			{
				Erguer(n1, parede);
				zPago = f.Zeni;
				Tirar(n1);
				AfirmarBl("(a constante do reembolso LIGADA em 0,5 -- em jogo ela e zero) desmanchar devolve metade do que a parede custou",
						  !DePe(n1) && Tem(zPago + pParede / 2, rPago), Bolsos());
				Tirar(n2);
				AfirmarBl("...na MOEDA em que foi paga: a parede paga em recursos devolve recursos, e nao zeni",
						  !DePe(n2) && Tem(zPago + pParede / 2, rPago + rParede / 2), Bolsos());

				double zAdm = adm.Ficha.Zeni;
				zPago = f.Zeni;
				rPago = f.Recursos;
				ComandoDeBloco(adm, Protocol.BlocoDesmanchar, n3.X, n3.Y, 0, "");
				AfirmarBl("...e so pro DONO: o admin que desmancha a parede de outro nao recebe, e o dono dela tambem nao",
						  !DePe(n3) && Math.Abs(adm.Ficha.Zeni - zAdm) < 1e-6 && Tem(zPago, rPago), $"{Bolsos()} | admin {adm.Ficha.Zeni:0.##}");
			}
			finally { PrecoDeBloco.DevolveAoDesmanchar = reembolsoDeAntes; }

			// ------------------------------------------------------------ a base de antes do preco
			// O ARQUIVO COMO ELE ERA: um `bases.json` gravado pelo servidor de antes desta mudanca nao tem `Pago`
			// nem `PagoEm`. Sai da gravacao de producao, com os dois campos tirados.
			ZerarBases();
			(int X, int Y) velha = (px + 1, py + 1), nova = (px + 2, py + 1);
			GuardarBloco(new BlocoDePe
			{
				Tipo = parede.Id, Def = parede, ZonaTipo = terra.Kind, ZonaNome = terra.Name, ZonaSeed = terra.Seed,
				X = velha.X, Y = velha.Y, DonoConta = "bancada_pagador", DonoNome = "bl: o pagador", Resistencia = 160,
			});
			GravarBasesEm(tmp);
			JsonNode? arquivo = JsonNode.Parse(System.IO.File.ReadAllText(tmp));
			if (arquivo is JsonArray gravados)
				foreach (JsonNode? bloco in gravados)
				{
					bloco?.AsObject().Remove("Pago");
					bloco?.AsObject().Remove("PagoEm");
				}
			string deAntes = arquivo?.ToJsonString() ?? "[]";
			System.IO.File.WriteAllText(tmp, deAntes);
			ZerarBases();
			AfirmarBl("(montagem) um `bases.json` no formato de antes do preco: uma parede do pagador, sem `Pago` nem `PagoEm`",
					  deAntes.Contains("bancada_pagador", StringComparison.Ordinal) && !deAntes.Contains("Pago", StringComparison.Ordinal)
					  && !DePe(velha), deAntes.Length > 200 ? deAntes[..200] : deAntes);

			Bolso(1000, 1000);
			CarregarBasesDeDisco(tmp);
			BlocoDePe? antiga = BlocoEm(terra, velha.X, velha.Y);
			AfirmarBl("A BASE QUE JA EXISTIA volta de pe, do mesmo dono, barrando -- e NAO cobra nada de ninguem",
					  antiga != null && antiga.DonoConta == "bancada_pagador" && mapa.BlockedCell(velha.X, velha.Y)
					  && antiga.Pago == 0 && Tem(1000, 1000), Bolsos());
			Erguer(nova, parede);
			AfirmarBl("...enquanto a MESMA parede, erguida agora ao lado dela pelo funil, cobra (o contraste: so erguer cobra)",
					  DePe(nova) && Tem(1000 - pParede, 1000), Bolsos());

			PrecoDeBloco.DevolveAoDesmanchar = 0.5;
			try
			{
				Tirar(velha);
				AfirmarBl("(reembolso ligado) desmanchar a parede antiga, que nao custou nada, nao rende nada",
						  !DePe(velha) && Tem(1000 - pParede, 1000), Bolsos());
			}
			finally { PrecoDeBloco.DevolveAoDesmanchar = reembolsoDeAntes; }
		}
		finally
		{
			ObraDeGracaDeTeste = RecursoSemDobroDeTeste = ObraFiadaDeTeste = ObraCompletaComAOutraDeTeste = false;
			MoedaDeObraNaoFicaDeTeste = SaldoSoQuandoSobeDeTeste = MesmaPecaCobraDeNovoDeTeste = false;
			PrecoDeBloco.DevolveAoDesmanchar = reembolsoDeAntes;
			EscutaDeSaldoDeObra = null;
			_moedaDeObra.Clear();   // os ids dos corpos forjados sao reusados: a escolha de um nao pode sobrar pro proximo
			if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);
		}
	}
}
