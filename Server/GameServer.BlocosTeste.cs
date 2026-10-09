using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA CONSTRUCAO DE BASE (`--blocoteste`) -- os quatro pedidos do dono de 2026-10-09 que moram no
/// servidor, cada um pelo funil de producao e cada um com o seu contra-exemplo:
///
///   * *"construcao de base (paredes, pisos e portas)"*: erguer, recusar, trocar, desmanchar, derrubar;
///   * *"se nao tiver [senha] qualquer um q passar nela ela abre, se tiver, ela e basicamente uma parede q ao
///     chegar perto vc pode por a senha pra ela abrir"*;
///   * *"a possibilidade de destruir portas"* -- a porta do MAPA que cai e nao tranca de novo;
///   * a macieira: *"ao elas serem destruidas elas criam um bloco de terra q tem colisao"*.
///
/// No boot, como as outras de servidor: precisa das zonas com colisao e de ninguem logado -- os corpos sao
/// forjados (sem `Peer`) e o que sairia no fio e lido pela <see cref="EscutaDeBlocos"/>. A metade do CLIENTE
/// (a barra, o fantasma, a pergunta da senha, o desenho) e o robo `--diagconstruir`.
///
/// O PALCO E A TERRA DE VERDADE, e tudo o que a bancada mexe volta no `finally`: os blocos de antes, a
/// porta do Banco, a macieira e o relogio.
/// </summary>
public sealed partial class GameServer
{
	private int _blOk, _blFalhou;

	private void AfirmarBl(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _blOk++; GD.Print($"[bloco]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _blFalhou++; GD.PrintErr($"[bloco]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDosBlocos()
	{
		_blOk = _blFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[bloco] ================ A CONSTRUCAO DE BASE (pedido do dono, 2026-10-09) ================");

		ZoneKey terra = ZonaDaBancadaDeProjetil;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra), vista = MapaDaVista(terra);
		List<BlocoDePe> deAntes = [.. _bases.Values.SelectMany(z => z.Celulas.Values)];
		long relogioDeAntes = AdiantoDoRelogioDeTeste;

		try
		{
			AfirmarBl("(montagem) o catalogo de blocos carregou com as tres classes",
					  _blocos is { Total: > 0 } && Bloco(ClasseDeBloco.Parede) != null && Bloco(ClasseDeBloco.Piso) != null
					  && Bloco(ClasseDeBloco.Porta) != null, $"{_blocos?.Total ?? 0} blocos");
			if (mapa == null || vista == null || _blocos is not { Total: > 0 }) { AfirmarBl("(montagem) a Terra tem os dois mapas", false); return; }
			ZerarBases();

			(int px, int py) = PracaDaBancada(mapa);
			const int T = ZoneCollision.TileSize;
			Vec2 No(int cx, int cy) => new(cx * T + T / 2f, cy * T + T / 2f - MoveRules.FeetOffsetY);

			ServerPlayer dono = Forjar("bl: o dono", No(px + 3, py + 4), 1_000);
			dono.Conta = "bancada_dono";
			ServerPlayer outro = Forjar("bl: o estranho", No(px + 5, py + 4), 1_000);
			outro.Conta = "bancada_estranho";

			BlocoDef parede = Bloco(ClasseDeBloco.Parede)!, piso = Bloco(ClasseDeBloco.Piso)!, porta = Bloco(ClasseDeBloco.Porta)!;

			// ------------------------------------------------------------ 1) ERGUER
			GD.Print("[bloco] -- 1) erguer: a parede barra, cega e cobre; o piso so cobre --");
			(int wx, int wy) = (px + 3, py + 2);
			ComandoDeBloco(dono, Protocol.BlocoErguer, wx, wy, parede.Numero, "");
			BlocoDePe? w = BlocoEm(terra, wx, wy);
			AfirmarBl("a parede sobe: esta na lista, e do dono, e a celula barra no mapa de colisao",
					  w != null && w.DonoConta == "bancada_dono" && mapa.BlockedCell(wx, wy));
			AfirmarBl("...cega (o mapa de visao barra a mesma celula)", vista.BlockedCell(wx, wy));
			AfirmarBl("...esta sob teto, e por isso barra quem voa (`ClasseDePredio.BarraQuemVoa`)",
					  CelulaSobTeto(terra, wx, wy) && ClasseDePredio.BarraQuemVoa(mapa, wx, wy));
			AfirmarBl("...com a resistencia de quem ergueu: o `intBPcap`, nunca menos que o 20 do cenario",
					  w != null && w.Resistencia >= Empurrao.ResistenciaPadrao
					  && Math.Abs(w.Resistencia - Math.Max(Empurrao.ResistenciaPadrao,
						  TetoDeTecnologia.De(dono.Ficha.relBPmax, dono.Ficha.techskill, dono.Ficha.techmod))) < 1e-6,
					  $"resistencia {w?.Resistencia:0.##}, relBPmax {dono.Ficha.relBPmax:0.##}");

			BlocoNaoAssentaDeTeste = true;
			try
			{
				ComandoDeBloco(dono, Protocol.BlocoErguer, wx + 1, wy, parede.Numero, "");
				AfirmarBl("(defeito injetado: o bloco nao assenta no mapa) a parede esta na lista e NAO barra",
						  BlocoEm(terra, wx + 1, wy) != null && !mapa.BlockedCell(wx + 1, wy));
			}
			finally { BlocoNaoAssentaDeTeste = false; }
			ComandoDeBloco(dono, Protocol.BlocoDesmanchar, wx + 1, wy, 0, "");

			(int fx, int fy) = (px + 2, py + 4);
			ComandoDeBloco(dono, Protocol.BlocoErguer, fx, fy, piso.Numero, "");
			AfirmarBl("o piso sobe sem barrar e sem cegar, e cobre a celula",
					  BlocoEm(terra, fx, fy) != null && !mapa.BlockedCell(fx, fy) && !vista.BlockedCell(fx, fy)
					  && CelulaSobTeto(terra, fx, fy));

			// ------------------------------------------------------------ 2) AS RECUSAS
			GD.Print("[bloco] -- 2) onde NAO sobe --");
			AfirmarBl("longe demais (alem de 4 celulas) nao sobe",
					  RecusaDeErguer(dono, parede, px + 3 + RegrasDeBloco.Alcance + 1, py + 4) == RecusaDeBloco.Longe);
			AfirmarBl("em cima de quem construi nao sobe parede -- e sobe piso",
					  RecusaDeErguer(dono, parede, px + 3, py + 4) == RecusaDeBloco.Corpo
					  && RecusaDeErguer(dono, piso, px + 3, py + 4) == RecusaDeBloco.Pode);
			AfirmarBl("o estranho nao constroi por cima do que e do dono; o dono troca o proprio",
					  RecusaDeErguer(outro, piso, wx, wy) == RecusaDeBloco.DeOutro
					  && RecusaDeErguer(dono, piso, wx, wy) == RecusaDeBloco.Pode);

			// o Banco da Terra: a parede sul (76,262) e cenario de pe E predio do mapa; o piso (73,260) e predio
			dono.Pos = No(76, 264);
			AfirmarBl("dentro do predio que o mapa trouxe nao se constroi (o Banco da Terra)",
					  RecusaDeErguer(dono, piso, 76, 261) == RecusaDeBloco.Predio
					  && RecusaDeErguer(dono, piso, 76, 262) == RecusaDeBloco.Predio,
					  $"{RecusaDeErguer(dono, piso, 76, 261)} / {RecusaDeErguer(dono, piso, 76, 262)}");
			(int ax, int ay)? agua = AguaPerto(mapa, px, py);
			if (agua is { } a)
			{
				dono.Pos = No(a.ax, a.ay + 1);
				AfirmarBl("na agua nao se constroi", RecusaDeErguer(dono, piso, a.ax, a.ay) == RecusaDeBloco.Agua,
						  $"({a.ax},{a.ay}): {RecusaDeErguer(dono, piso, a.ax, a.ay)}");
			}
			(int mx, int my)? muro = MuroSoltoPerto(mapa, px, py);
			if (muro is { } m)
			{
				dono.Pos = No(m.mx, m.my + 2);
				AfirmarBl("cenario de pe nao se troca por bloco: derruba-se primeiro",
						  RecusaDeErguer(dono, piso, m.mx, m.my) == RecusaDeBloco.Cenario,
						  $"({m.mx},{m.my}): {RecusaDeErguer(dono, piso, m.mx, m.my)}");
			}
			dono.Pos = No(px + 3, py + 4);

			// ------------------------------------------------------------ 3) DESMANCHAR
			GD.Print("[bloco] -- 3) desmanchar: so o dono, e o chao volta --");
			ComandoDeBloco(outro, Protocol.BlocoDesmanchar, wx, wy, 0, "");
			AfirmarBl("o estranho NAO desmancha a parede do dono", BlocoEm(terra, wx, wy) != null);
			QualquerUmDesmanchaDeTeste = true;
			try
			{
				ComandoDeBloco(outro, Protocol.BlocoDesmanchar, fx, fy, 0, "");
				AfirmarBl("(defeito injetado: qualquer um desmancha) o estranho tira o piso do dono", BlocoEm(terra, fx, fy) == null);
			}
			finally { QualquerUmDesmanchaDeTeste = false; }
			ComandoDeBloco(dono, Protocol.BlocoDesmanchar, wx, wy, 0, "");
			AfirmarBl("o dono desmancha: a celula deixa de barrar, de cegar e de estar sob teto, e nao vira estrago",
					  BlocoEm(terra, wx, wy) == null && !mapa.BlockedCell(wx, wy) && !vista.BlockedCell(wx, wy)
					  && !CelulaSobTeto(terra, wx, wy) && !Caiu(terra, wx, wy));

			// ------------------------------------------------------------ 4) O SOCO
			GD.Print("[bloco] -- 4) o soco: abaixo da resistencia nunca, a partir dela com 34% por golpe --");
			ComandoDeBloco(dono, Protocol.BlocoErguer, wx, wy, parede.Numero, "");
			double r = BlocoEm(terra, wx, wy)!.Resistencia;
			ServerPlayer fraco = Forjar("bl: o fraco", No(wx, wy + 1), 100);
			fraco.Conta = "bancada_fraco";
			fraco.Facing = Facing.North;
			AfirmarBl("(montagem) o fraco expressa menos que a resistencia da parede, e mais que o 20 do cenario",
					  fraco.Ficha.expressedBP < r && fraco.Ficha.expressedBP >= Empurrao.ResistenciaPadrao,
					  $"BP {fraco.Ficha.expressedBP:0} contra resistencia {r:0}");
			for (int i = 0; i < 80; i++) SocarCenario(fraco);
			AfirmarBl("80 socos de quem nao alcanca a resistencia: a parede fica de pe", BlocoEm(terra, wx, wy) != null);

			BlocoCaiComoCenarioDeTeste = true;
			try
			{
				for (int i = 0; i < 80 && BlocoEm(terra, wx, wy) != null; i++) SocarCenario(fraco);
				AfirmarBl("(defeito injetado: o bloco cai pelo 20 do cenario) o fraco derruba a parede do dono",
						  BlocoEm(terra, wx, wy) == null);
			}
			finally { BlocoCaiComoCenarioDeTeste = false; }
			DesfazerQueda(terra, wx, wy);

			ComandoDeBloco(dono, Protocol.BlocoErguer, wx, wy, parede.Numero, "");
			ServerPlayer forte = Forjar("bl: o forte", No(wx, wy + 1), 100);
			forte.Conta = "bancada_forte";
			forte.Facing = Facing.North;
			forte.Ficha.BP = r * 50;
			forte.Ficha.Tick(agoraMs: NowMs());
			fraco.Pos = No(px + 7, py + 4);
			int socos = 0;
			while (socos < 80 && BlocoEm(terra, wx, wy) != null) { SocarCenario(forte); socos++; }
			AfirmarBl("quem alcanca a resistencia derruba em poucos socos, e a celula vira estrago da zona (terra batida)",
					  BlocoEm(terra, wx, wy) == null && !mapa.BlockedCell(wx, wy) && Caiu(terra, wx, wy),
					  $"{socos} socos, BP {forte.Ficha.expressedBP:0}");
			ComandoDeBloco(dono, Protocol.BlocoErguer, wx, wy, parede.Numero, "");
			AfirmarBl("em cima da terra batida o dono ergue de novo, e a parede barra (a erguida vence o estrago)",
					  BlocoEm(terra, wx, wy) != null && mapa.BlockedCell(wx, wy));

			// ------------------------------------------------------------ 5) O CHAO QUE RACHA
			GD.Print("[bloco] -- 5) o chao que racha poupa o que tem dono; a tecnica que varre tudo, nao --");
			ComandoDeBloco(dono, Protocol.BlocoErguer, fx, fy, piso.Numero, "");
			var meio = new Vec2(wx * T + T / 2f, (wy + 1) * T + T / 2f);
			RacharChao(terra, meio, r * 50, raio: 2, chance: 1.0);
			AfirmarBl("um impacto com forca de sobra racha o chao em volta e deixa a parede e o piso de pe (`!T.proprietor`)",
					  BlocoEm(terra, wx, wy) != null && BlocoEm(terra, fx, fy) != null && Caiu(terra, wx + 1, wy + 1));
			RacharChao(terra, meio, r * 50, raio: 2, chance: 1.0, levaBlocos: true);
			AfirmarBl("a varredura do Berserker leva a parede e o piso junto (`expressedBP >= T.Resistance`)",
					  BlocoEm(terra, wx, wy) == null && BlocoEm(terra, fx, fy) == null);

			// ------------------------------------------------------------ 6) A PORTA SEM SENHA
			GD.Print("[bloco] -- 6) a porta sem senha: parede fechada, abre pra quem encosta, fecha em 5 s --");
			forte.Pos = No(px + 8, py + 4);
			(int dx, int dy) = (px + 5, py + 2);
			ComandoDeBloco(dono, Protocol.BlocoErguer, dx, dy, porta.Numero, "");
			BlocoDePe? p = BlocoEm(terra, dx, dy);
			AfirmarBl("a porta nasce fechada: barra, cega e nao tem senha",
					  p != null && p.Senha.Length == 0 && mapa.BlockedCell(dx, dy) && vista.BlockedCell(dx, dy));
			Encostar(outro, No(dx, dy + 1), Facing.North);
			TickDasPortasErguidas();
			AfirmarBl("o estranho anda contra ela e ela abre: a celula deixa de barrar e de cegar",
					  p != null && p.AbertaAte != 0 && !mapa.BlockedCell(dx, dy) && !vista.BlockedCell(dx, dy));
			outro.Moving = false;
			outro.Pos = No(dx, dy);   // parado NO vao
			AdiantoDoRelogioDeTeste += PortaFechaEmMs + 1000;
			TickDasPortasErguidas();
			AfirmarBl("passado o prazo com alguem no vao, ela NAO fecha em cima dele", p != null && p.AbertaAte != 0 && !mapa.BlockedCell(dx, dy));
			outro.Pos = No(px + 5, py + 4);
			AdiantoDoRelogioDeTeste += 1000;
			TickDasPortasErguidas();
			AfirmarBl("vao livre: ela fecha sozinha e volta a ser parede", p != null && p.AbertaAte == 0 && mapa.BlockedCell(dx, dy) && vista.BlockedCell(dx, dy));

			// ------------------------------------------------------------ 7) A PORTA COM SENHA
			GD.Print("[bloco] -- 7) a porta com senha: e uma parede, ate alguem digitar a senha nela --");
			ComandoDeBloco(dono, Protocol.BlocoDesmanchar, dx, dy, 0, "");
			ComandoDeBloco(dono, Protocol.BlocoErguer, dx, dy, porta.Numero, "  kame-123  ");
			p = BlocoEm(terra, dx, dy);
			AfirmarBl("a senha fica guardada limpa (sem os espacos das pontas)", p != null && p.Senha == "kame-123", $"'{p?.Senha}'");

			Encostar(outro, No(dx, dy + 1), Facing.North);
			TickDasPortasErguidas();
			AfirmarBl("o estranho anda contra a porta trancada e ela NAO abre", p != null && p.AbertaAte == 0 && mapa.BlockedCell(dx, dy));
			PortaSemTrancaDeTeste = true;
			try
			{
				TickDasPortasErguidas();
				AfirmarBl("(defeito injetado: porta sem tranca) a porta com senha abre pra quem encosta", p != null && p.AbertaAte != 0);
			}
			finally { PortaSemTrancaDeTeste = false; }
			outro.Moving = false;
			outro.Pos = No(dx + 1, dy + 1);   // ao lado: ao alcance da tecla E, fora do vao
			AdiantoDoRelogioDeTeste += PortaFechaEmMs + 1000;
			TickDasPortasErguidas();

			ComandoDePortaErguida(outro, "porta_senha", "goku");
			AfirmarBl("senha errada: continua trancada, e a porta nao passa a lembrar dele",
					  p != null && p.AbertaAte == 0 && !p.Sabem.Contains("bancada_estranho"));
			AdiantoDoRelogioDeTeste += GestoNaPortaACadaMs + 100;
			ComandoDePortaErguida(outro, "porta_senha", "kame-123");
			AfirmarBl("senha certa: a porta abre na hora e lembra de quem acertou",
					  p != null && p.AbertaAte != 0 && !mapa.BlockedCell(dx, dy) && p.Sabem.Contains("bancada_estranho"));
			AdiantoDoRelogioDeTeste += PortaFechaEmMs + 1000;
			TickDasPortasErguidas();
			Encostar(outro, No(dx, dy + 1), Facing.North);
			TickDasPortasErguidas();
			AfirmarBl("depois de fechada, quem ja digitou abre so por encostar", p != null && p.AbertaAte != 0);
			outro.Moving = false;
			outro.Pos = No(dx + 1, dy + 1);
			AdiantoDoRelogioDeTeste += PortaFechaEmMs + 1000;
			TickDasPortasErguidas();

			Encostar(dono, No(dx, dy + 1), Facing.North);
			TickDasPortasErguidas();
			AfirmarBl("o dono passa sem digitar", p != null && p.AbertaAte != 0);
			dono.Moving = false;
			dono.Pos = No(dx - 1, dy + 1);
			AdiantoDoRelogioDeTeste += PortaFechaEmMs + 1000;
			TickDasPortasErguidas();

			// o fio: as marcas sao de quem recebe, e a senha nao viaja
			EscutaDeBlocos = [];
			MandarBlocos(dono);
			MandarBlocos(outro);
			byte[] fioDoDono = EscutaDeBlocos.First(e => e.Para == dono.Id).Fio, fioDoOutro = EscutaDeBlocos.First(e => e.Para == outro.Id).Fio;
			EscutaDeBlocos = null;
			byte? marcaDoDono = MarcaNoRetrato(fioDoDono, terra, dx, dy), marcaDoOutro = MarcaNoRetrato(fioDoOutro, terra, dx, dy);
			AfirmarBl("no retrato do dono a porta vem marcada MINHA e TRANCADA; no do estranho que digitou, TRANCADA e SEI A SENHA",
					  marcaDoDono == (Protocol.BlocoMeu | Protocol.BlocoTrancado | Protocol.BlocoSeiASenha)
					  && marcaDoOutro == (Protocol.BlocoTrancado | Protocol.BlocoSeiASenha),
					  $"dono {marcaDoDono}, estranho {marcaDoOutro}");
			byte[] senhaNoFio = System.Text.Encoding.UTF8.GetBytes("kame-123");
			AfirmarBl("a senha NAO viaja em nenhum dos dois retratos", !Contem(fioDoDono, senhaNoFio) && !Contem(fioDoOutro, senhaNoFio));

			ComandoDePortaErguida(outro, "porta_trocar", "minha");
			AfirmarBl("o estranho nao troca a senha da porta do dono", p != null && p.Senha == "kame-123");
			dono.Pos = No(dx + 1, dy + 1);
			ComandoDePortaErguida(dono, "porta_trocar", "nova");
			Encostar(outro, No(dx, dy + 1), Facing.North);
			TickDasPortasErguidas();
			AfirmarBl("o dono troca a senha: quem sabia a antiga volta a dar com a parede",
					  p != null && p.Senha == "nova" && p.Sabem.Count == 0 && p.AbertaAte == 0);
			outro.Moving = false;

			AfirmarBl("a porta trancada cai na porrada como parede (o soco de quem alcanca a resistencia)", DerrubaASoco(forte, terra, dx, dy, No));

			// ------------------------------------------------------------ 8) O DISCO
			GD.Print("[bloco] -- 8) o disco: a base volta do arquivo com dono, senha e resistencia --");
			dono.Pos = No(px + 3, py + 4);
			outro.Pos = No(px + 5, py + 4);
			forte.Pos = No(px + 8, py + 4);
			ComandoDeBloco(dono, Protocol.BlocoErguer, dx, dy, porta.Numero, "volta");
			ComandoDeBloco(dono, Protocol.BlocoErguer, wx, wy - 1, parede.Numero, "");
			double rGravada = BlocoEm(terra, wx, wy - 1)?.Resistencia ?? -1;
			string tmp = System.IO.Path.Combine(OS.GetUserDataDir(), "bases-da-bancada.json");
			GravarBasesEm(tmp);
			ZerarBases();
			AfirmarBl("(montagem) zeradas as bases, as celulas voltam ao que o mapa diz",
					  BlocoEm(terra, dx, dy) == null && !mapa.BlockedCell(dx, dy) && !mapa.BlockedCell(wx, wy - 1));
			CarregarBasesDeDisco(tmp);
			BlocoDePe? pv = BlocoEm(terra, dx, dy), wv = BlocoEm(terra, wx, wy - 1);
			AfirmarBl("lido o arquivo: a porta volta trancada, fechada e do dono; a parede, com a mesma resistencia; as duas barram",
					  pv != null && pv.Senha == "volta" && pv.DonoConta == "bancada_dono" && pv.AbertaAte == 0
					  && wv != null && Math.Abs(wv.Resistencia - rGravada) < 1e-6
					  && mapa.BlockedCell(dx, dy) && mapa.BlockedCell(wx, wy - 1) && vista.BlockedCell(wx, wy - 1));

			// RELER O MUNDO COM O SERVIDOR DE PE (so a bancada do wipe faz, no `RecarregarMundoDoDisco`): o que
			// esta de pe e NAO esta no arquivo tem de sair -- a memoria fica igual ao disco.
			int gravados = BlocosDeTeste(terra), naConta = _blocosPorConta.GetValueOrDefault("bancada_dono");
			(int ex, int ey) = (px + 1, py + 2);
			ComandoDeBloco(dono, Protocol.BlocoErguer, ex, ey, parede.Numero, "");
			AfirmarBl("(montagem) uma terceira parede, erguida DEPOIS de gravar: ela nao esta no arquivo",
					  BlocoEm(terra, ex, ey) != null && mapa.BlockedCell(ex, ey));
			BasesLidasPorCimaDeTeste = true;
			try
			{
				CarregarBasesDeDisco(tmp);
				AfirmarBl("(defeito injetado: a leitura nao esvazia antes) relido o arquivo, a parede que nao esta nele continua de pe",
						  BlocoEm(terra, ex, ey) != null && mapa.BlockedCell(ex, ey));
			}
			finally { BasesLidasPorCimaDeTeste = false; }
			CarregarBasesDeDisco(tmp);
			AfirmarBl("relido o arquivo por cima do mundo de pe, fica so o que o DISCO diz: a terceira parede sai e as duas gravadas ficam",
					  BlocoEm(terra, ex, ey) == null && !mapa.BlockedCell(ex, ey)
					  && BlocoEm(terra, dx, dy) != null && BlocoEm(terra, wx, wy - 1) != null && BlocosDeTeste(terra) == gravados
					  && _blocosPorConta.GetValueOrDefault("bancada_dono") == naConta,
					  $"{BlocosDeTeste(terra)} bloco(s) de pe (eram {gravados}), {_blocosPorConta.GetValueOrDefault("bancada_dono")} na conta do dono (eram {naConta})");
			if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp);

			// ------------------------------------------------------------ 9) A PORTA DO MAPA
			PortaDoMapaQueCai(terra, mapa, forte, No);

			// ------------------------------------------------------------ 10) A MACIEIRA
			AMacieiraQueCai(terra, mapa);
		}
		finally
		{
			BlocoNaoAssentaDeTeste = PortaSemTrancaDeTeste = QualquerUmDesmanchaDeTeste = BlocoCaiComoCenarioDeTeste = false;
			EscutaDeBlocos = null;
			AdiantoDoRelogioDeTeste = relogioDeAntes;

			// O MUNDO VOLTA: os blocos de antes da bancada, e nada dela no disco.
			ZerarBases();
			foreach (BlocoDePe b in deAntes) GuardarBloco(b);
			foreach (BaseDaZona z in _bases.Values) AssentarBase(z);
			_basesSujas = false;
			LimparTudoDaBancada();   // os corpos forjados saem do mundo
		}

		GD.Print($"[bloco] ================ {_blOk} OK, {_blFalhou} FALHA(S) ================");
	}

	// =====================================================================
	// 9) A PORTA DO MAPA QUE CAI
	// =====================================================================
	/// <summary>
	/// *"outra coisa q falta no jogo e a possibilidade de destruir portas"*. A porta do Banco da Terra cai
	/// pelo soco de producao; depois alguem anda contra o vao e o prazo de fechar passa -- e ela tem de
	/// continuar aberta. Com o defeito injetado (o relogio das portas nao sabe da queda) ela tranca de novo.
	/// </summary>
	private void PortaDoMapaQueCai(ZoneKey terra, ZoneCollision mapa, ServerPlayer forte, Func<int, int, Vec2> no)
	{
		GD.Print("[bloco] -- 9) a porta do MAPA: cai, e nao tranca de novo --");
		if (!_portasDoMapa.TryGetValue(terra.Name, out List<PortaDoMapa>? portas) || portas.Count == 0)
		{ AfirmarBl("(montagem) a Terra tem porta de mapa", false); return; }
		PortaDoMapa d = portas[0];
		ZoneCollision? vista = MapaDaVista(terra);
		bool montada = mapa.BloqueadaNoArquivo(d.X, d.Y) && mapa.BlockedCell(d.X, d.Y) && !mapa.BlockedCell(d.X, d.Y + 1) && !Caiu(terra, d.X, d.Y);
		AfirmarBl("(montagem) a porta do Banco esta fechada, de pe, com chao livre ao sul", montada, $"({d.X},{d.Y})");
		if (!montada) return;

		void Repor()
		{
			if (_cenarioCaido.TryGetValue(terra.Name, out HashSet<(int X, int Y)>? c)) c.Remove((d.X, d.Y));
			if (_portasAbertas.TryGetValue(terra.Name, out Dictionary<(int X, int Y), long>? ab)) ab.Remove((d.X, d.Y));
			mapa.Fechar(d.X, d.Y);
			vista?.Fechar(d.X, d.Y);
		}

		// Quem anda contra a porta caida e deixa o prazo passar: ela tranca?
		bool TrancaDeNovo()
		{
			Encostar(forte, no(d.X, d.Y + 1), Facing.North);
			TickDasPortas();
			forte.Moving = false;
			forte.Pos = no(d.X, d.Y + 3);
			AdiantoDoRelogioDeTeste += PortaFechaEmMs + 1000;
			TickDasPortas();
			return mapa.BlockedCell(d.X, d.Y);
		}

		try
		{
			AfirmarBl("o soco de quem alcanca a resistencia do cenario derruba a porta fechada",
					  DerrubaASoco(forte, terra, d.X, d.Y, no) && !mapa.BlockedCell(d.X, d.Y) && Caiu(terra, d.X, d.Y)
					  && vista?.BlockedCell(d.X, d.Y) != true);
			AfirmarBl("alguem anda contra a porta caida e o prazo passa: ela continua aberta", !TrancaDeNovo());

			Repor();
			PortaCaidaTrancaDeNovoDeTeste = true;
			try
			{
				DerrubaASoco(forte, terra, d.X, d.Y, no);
				AfirmarBl("(defeito injetado: a porta caida continua no relogio das portas) ela tranca de novo, cinco segundos depois",
						  TrancaDeNovo());
			}
			finally { PortaCaidaTrancaDeNovoDeTeste = false; }
		}
		finally
		{
			PortaCaidaTrancaDeNovoDeTeste = false;
			Repor();
		}
	}

	// =====================================================================
	// 10) A MACIEIRA QUE CAI
	// =====================================================================
	/// <summary>
	/// *"ao elas serem destruidas elas criam um bloco de terra q tem colisao ai vc tem q quebrar esse bloco
	/// de terra tb"*. Uma macieira DO MAPA cai pelo `Estragar` de producao: a celula dela tem de deixar de
	/// barrar no servidor e entrar no retrato de quem chega. Com o defeito injetado (so o aviso sai) a
	/// celula continua barrando -- o bloco de terra do dono.
	/// </summary>
	private void AMacieiraQueCai(ZoneKey terra, ZoneCollision mapa)
	{
		GD.Print("[bloco] -- 10) a macieira do mapa: cai, e a celula dela cai junto --");
		Obra? arvore = _noChao.FirstOrDefault(o => o.DoMapa && o.Tipo == "AppleTree" && o.Zona.Equals(terra));
		ZoneEntry? entrada = _catalogo?.Get(terra);
		AfirmarBl("(montagem) a Terra tem uma macieira do mapa de pe", arvore != null && entrada != null);
		if (arvore == null || entrada == null) return;

		(int cx, int cy) = CatalogoDeObras.Celula(arvore.X, arvore.Y);
		AfirmarBl("(montagem) a celula dela barra pelo bit do `.col` -- e ele que sobrava", mapa.BloqueadaNoArquivo(cx, cy) && mapa.BlockedCell(cx, cy),
				  $"({cx},{cy})");

		void Repor()
		{
			if (_cenarioCaido.TryGetValue(terra.Name, out HashSet<(int X, int Y)>? c)) c.Remove((cx, cy));
			mapa.Fechar(cx, cy);
			MapaDaVista(terra)?.Fechar(cx, cy);
		}

		try
		{
			Estragar(arvore, 1_000_000, null);
			AfirmarBl("derrubada a macieira, a celula dela NAO barra mais no servidor",
					  ObraNaCelula(terra, cx, cy) == null && !mapa.BlockedCell(cx, cy));
			AfirmarBl("...e entra no retrato de quem chega depois (a terra batida aparece pra todo mundo)",
					  LerPacoteDeCenario(PacoteDeRetratoDeCenario(terra).CopyData()).Celulas.Contains((cx, cy)));

			Repor();
			(int postas, _) = PorMobiliaDoMapa(entrada, soAQueFalta: true);
			Obra? deVolta = ObraNaCelula(terra, cx, cy);
			AfirmarBl("refeito o cenario, a macieira que faltava volta ao lugar (e so ela)",
					  postas == 1 && deVolta is { DoMapa: true, Tipo: "AppleTree" } && mapa.BlockedCell(cx, cy), $"{postas} reposta(s)");

			ObraCaidaSoAvisaDeTeste = true;
			try
			{
				if (deVolta != null) Estragar(deVolta, 1_000_000, null);
				AfirmarBl("(defeito injetado: a obra cai e o servidor so avisa) sobra a celula barrando -- o 'bloco de terra com colisao'",
						  ObraNaCelula(terra, cx, cy) == null && mapa.BlockedCell(cx, cy));
			}
			finally { ObraCaidaSoAvisaDeTeste = false; }
		}
		finally
		{
			ObraCaidaSoAvisaDeTeste = false;
			Repor();
			PorMobiliaDoMapa(entrada, soAQueFalta: true);
		}
	}

	// =====================================================================
	// AJUDANTES
	// =====================================================================
	private BlocoDef? Bloco(ClasseDeBloco classe) => _blocos?.Todos.FirstOrDefault(b => b.Classe == classe);

	private bool Caiu(ZoneKey zona, int cx, int cy) =>
		_cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? c) && c.Contains((cx, cy));

	/// <summary>Tira uma celula do estrago da zona (a bancada devolve o chao que ela mesma rachou).</summary>
	private void DesfazerQueda(ZoneKey zona, int cx, int cy)
	{
		if (_cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? c)) c.Remove((cx, cy));
		MapaDaZonaOuCatalogo(zona)?.Fechar(cx, cy);
		MapaDaVista(zona)?.Fechar(cx, cy);
	}

	/// <summary>Poe o corpo num ponto, virado, ANDANDO -- o que o relogio das portas le como "encostou".</summary>
	private static void Encostar(ServerPlayer pl, Vec2 onde, Facing rumo)
	{
		pl.Pos = onde;
		pl.Facing = rumo;
		pl.Moving = true;
	}

	/// <summary>Soca a celula ao norte, de um tile ao sul, ate 80 vezes. Devolve se ela deixou de barrar.</summary>
	private bool DerrubaASoco(ServerPlayer quem, ZoneKey zona, int cx, int cy, Func<int, int, Vec2> no)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) return false;
		quem.Pos = no(cx, cy + 1);
		quem.Facing = Facing.North;
		for (int i = 0; i < 80 && mapa.BlockedCell(cx, cy); i++) SocarCenario(quem);
		return !mapa.BlockedCell(cx, cy);
	}

	/// <summary>
	/// A PRACA DA BANCADA: um retangulo de 10x6 de chao comum na Terra -- sem parede, agua, predio do mapa,
	/// obra, nem estrago. Devolve o canto de cima.
	/// </summary>
	private (int X, int Y) PracaDaBancada(ZoneCollision mapa)
	{
		for (int tentativa = 0; tentativa < 60; tentativa++)
		{
			Vec2 c = CorredorLivre(10);
			int x0 = (int)(c.X / ZoneCollision.TileSize), y0 = (int)(c.Y / ZoneCollision.TileSize);
			bool serve = true;
			for (int y = y0; y < y0 + 6 && serve; y++)
				for (int x = x0; x < x0 + 10 && serve; x++)
					serve = mapa.ServeDeChao(x, y) && !mapa.NasceuDentro(x, y) && !mapa.Indestrutivel(x, y) && !mapa.Selada(x, y)
							&& ObraNaCelula(ZonaDaBancadaDeProjetil, x, y) == null && !Caiu(ZonaDaBancadaDeProjetil, x, y);
			if (serve) { GD.Print($"[bloco] a praca da bancada: ({x0},{y0}) a ({x0 + 9},{y0 + 5})"); return (x0, y0); }
		}
		AfirmarBl("(montagem) achei uma praca de 10x6 de chao comum na Terra", false);
		return (8, 8);
	}

	private static (int X, int Y)? AguaPerto(ZoneCollision mapa, int x0, int y0)
	{
		for (int r = 1; r < 200; r++)
			for (int y = y0 - r; y <= y0 + r; y++)
				for (int x = x0 - r; x <= x0 + r; x++)
					if ((Math.Abs(x - x0) == r || Math.Abs(y - y0) == r) && mapa.EhAgua(x, y) && !mapa.NaBorda(x, y)) return (x, y);
		return null;
	}

	/// <summary>Uma celula de cenario de pe que nao e predio do mapa (um muro solto, uma arvore-tile), com chao ao sul.</summary>
	private (int X, int Y)? MuroSoltoPerto(ZoneCollision mapa, int x0, int y0)
	{
		for (int r = 1; r < 200; r++)
			for (int y = y0 - r; y <= y0 + r; y++)
				for (int x = x0 - r; x <= x0 + r; x++)
					if ((Math.Abs(x - x0) == r || Math.Abs(y - y0) == r) && mapa.BloqueadaNoArquivo(x, y) && !mapa.NasceuDentro(x, y)
						&& !mapa.NaBorda(x, y) && !mapa.Indestrutivel(x, y) && !mapa.EhAgua(x, y) && !Caiu(ZonaDaBancadaDeProjetil, x, y))
						return (x, y);
		return null;
	}

	/// <summary>As marcas de um bloco dentro de um retrato (`S2C.Blocos`, modo retrato), ou nulo se ele nao esta la.</summary>
	private static byte? MarcaNoRetrato(byte[] fio, ZoneKey zona, int cx, int cy)
	{
		var r = new NetDataReader(fio);
		if (r.GetByte() != (byte)Protocol.S2C.Blocos || r.GetByte() != Protocol.BlocosRetrato || r.GetULong() != zona.Hash) return null;
		int n = r.GetInt();
		for (int i = 0; i < n; i++)
		{
			int x = r.GetUShort(), y = r.GetUShort();
			r.GetUShort();
			byte marcas = r.GetByte();
			if (x == cx && y == cy) return marcas;
		}
		return null;
	}

	private static bool Contem(byte[] onde, byte[] oQue)
	{
		for (int i = 0; i + oQue.Length <= onde.Length; i++)
		{
			int j = 0;
			while (j < oQue.Length && onde[i + j] == oQue[j]) j++;
			if (j == oQue.Length) return true;
		}
		return false;
	}
}
