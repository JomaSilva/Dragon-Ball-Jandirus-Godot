using System.Text.Json;
using Godot;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--snakewayteste` (2026-10-27) -- o pedido 124d: o MORRER de proposito e a SNAKE WAY. Corpos forjados (conta, sem `Peer`), com os
/// ganchos de combate de producao (`PrepararCombate`), no Outro Mundo de verdade (a mesa do Enma e o Sr. Kaioh que o boot semeia):
///   W1  MORRER: o vivo morre pelo funil de toda morte (o relogio da viagem pro Outro Mundo arma), sem Zenkai (o BP fica) e sem mexer no
///       karma; o morto ouve que ja esta morto;
///   W2  o ENMA tem "Correr a Snake Way": abaixo do minimo de BP, recusa; o vivo, recusa; com o minimo, a corrida comeca;
///   W3  VOAR CANCELA: quem voa no meio da corrida a perde;
///   W4  (o pedido 142) A CHEGADA E O FIM DA ESTRADA: no mapa de verdade, da mesa do Enma ha caminho A PE ate a cabeca da serpente -- e ate
///       o Sr. Kaioh (a chegada de antes) NAO ha, a ilha fica a 11 tiles de nuvem; a pe na cabeca a corrida fecha: +1.000 de BP uma vez e
///       a marca, que vai e volta do save; a segunda corrida e recusada;
///   W5  o SR. KAIOH so ensina o Kaio-ken a quem correu (o mesmo corpo, com karma e BP de sobra: sem a marca, nao; com ela, sim).
///
///     Godot --headless --path . --server --port 11851 --snakewayteste
/// </summary>
public sealed partial class GameServer
{
	private int _swOk, _swFalhou;

	private void AfirmarSw(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _swOk++; GD.Print($"[snakeway]   OK    {oque}   [{detalhe}]"); return; }
		_swFalhou++;
		GD.PrintErr($"[snakeway]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaSnakeWay()
	{
		_swOk = _swFalhou = 0;
		GD.Print("[snakeway] ================ MORRER E A SNAKE WAY (o pedido 124d, 2026-10-27) ================");
		const int idBase = 992_760;
		const float t = ZoneCollision.TileSize;
		var forjados = new List<ServerPlayer>();
		ZoneKey terra = ZoneKey.Premade("Earth");
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		ServerPlayer Forjar(int i, string nome, double bp)
		{
			var p = new ServerPlayer
			{
				Id = idBase + i, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 25, Zone = terra,
				Pos = new Vec2(i * 3 * t, 0), Conta = $"bancada_snakeway_{i}", Slot = 0,
				Ficha = new Fighter { Race = "Human", BP = bp }, Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			p.Ficha.Class = "Normal";
			PorNoMundo(p);
			PrepararCombate(p, null);
			forjados.Add(p);
			return p;
		}
		// AO LADO DA OBRA DO ENMA (um tile ao sul da cadeira): a chegada do morto (`MesaDoEnma`) nao fica ao alcance do menu dele
		Vec2 AoLadoDoEnma() => _noChao.FirstOrDefault(o => o.Tipo == Alem.TipoDoEnma && o.Zona.Equals(alem)) is { } enma
			? new Vec2(enma.X, enma.Y + t) : MesaDoEnma(alem);
		void MortoNaMesa(ServerPlayer p) { p.Ficha.dead = true; p.MorteJaViajou = true; MoveToZone(p.Id, alem, AoLadoDoEnma()); }
		Vec2 AoLadoDoKaioh() => new(NpcsDeConversa.SrKaiohX * t + t / 2f, (NpcsDeConversa.SrKaiohY + 1) * t + t / 2f);
		// (2026-10-27, o pedido 142) na estrada nove fileiras antes do fim, e na cabeca da serpente -- as celulas do MAPA, escritas aqui
		// (a estrada tem cinco de largura e termina na fileira 50 do `z06_Afterlife`): a contraprova muda o `SnakeWay.FimX/FimY`, e uma regua
		// que se posicionasse por eles iria atras da chegada mutada
		Vec2 NaEstrada() => Caminho.Centro(176, 61);
		Vec2 NaCabeca() => Caminho.Centro(176, 52);
		// A PE NO MAPA DE VERDADE: as celulas que se alcancam andando da mesa do Enma -- sem parede do arquivo (a porta do mapa conta: ela
		// abre), sem nuvem (a pe ela joga no Inferno) e sem agua
		HashSet<(int X, int Y)> APeDoEnma()
		{
			ZoneCollision mapa = MapaDaZonaOuCatalogo(alem)!;
			var portas = new HashSet<(int X, int Y)>(_portasDoMapa.TryGetValue(Alem.ZonaDoOutroMundo, out List<PortaDoMapa>? ps)
				? ps.Select(p => (p.X, p.Y)) : []);
			bool Anda(int x, int y) => x >= 0 && y >= 0 && x < mapa.Width && y < mapa.Height && !mapa.EhNuvem(x, y) && !mapa.EhAgua(x, y)
									   && (!mapa.BloqueadaNoArquivo(x, y) || portas.Contains((x, y)));
			(int X, int Y) inicio = Caminho.Celula(AoLadoDoEnma());
			var vistas = new HashSet<(int X, int Y)> { inicio };
			var fila = new Queue<(int X, int Y)>();
			fila.Enqueue(inicio);
			while (fila.Count > 0)
			{
				(int x, int y) = fila.Dequeue();
				foreach ((int dx, int dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
					if (Anda(x + dx, y + dy) && vistas.Add((x + dx, y + dy))) fila.Enqueue((x + dx, y + dy));
			}
			return vistas;
		}
		try
		{
			// ---- W1) morrer
			ServerPlayer vivo = Forjar(1, "bancada: o que morre", 5_000);
			int karmaAntes = vivo.Karma;
			double bpAntes = vivo.Ficha.BP;
			List<string> morte = Ouvir(() => Verbo(vivo, "morrer", ""));
			AfirmarSw("W1. MORRER: o vivo morre pelo funil de toda morte (o relogio da viagem pro Outro Mundo arma), sem Zenkai e sem karma",
					  vivo.Ficha.dead && vivo.RelogioDaMorte > NowMs() && Math.Abs(vivo.Ficha.BP - bpAntes) < 1e-6 && vivo.Karma == karmaAntes,
					  $"morto {vivo.Ficha.dead}, relogio {vivo.RelogioDaMorte - NowMs()} ms, BP {bpAntes:0} -> {vivo.Ficha.BP:0}, karma {karmaAntes} -> {vivo.Karma}; {string.Join(" | ", morte)}");
			List<string> deNovo = Ouvir(() => Verbo(vivo, "morrer", ""));
			AfirmarSw("W1b. ...e o MORTO ouve que ja esta morto (o contra-exemplo)", deNovo.Any(s => s.Contains("já está morto")), string.Join(" | ", deNovo));

			// ---- W2) o Enma
			bool noMenu = Jandirus.Core.Tech.Interacoes.De("Enma_Daioh").Any(a => a.Verbo == SnakeWay.Verbo);
			ServerPlayer fraco = Forjar(2, "bancada: o fraco", SnakeWay.BpMinimo / 2);
			MortoNaMesa(fraco);
			List<string> recusaFraco = Ouvir(() => Verbo(fraco, SnakeWay.Verbo, ""));
			ServerPlayer visitante = Forjar(3, "bancada: o vivo na mesa", SnakeWay.BpMinimo * 5);
			MoveToZone(visitante.Id, alem, AoLadoDoEnma());
			List<string> recusaVivo = Ouvir(() => Verbo(visitante, SnakeWay.Verbo, ""));
			ServerPlayer corredor = Forjar(4, "bancada: o corredor", SnakeWay.BpMinimo * 2);
			MortoNaMesa(corredor);
			bool aoAlcance = ObraQueAceita(corredor, SnakeWay.Verbo) != null;
			List<string> largada = Ouvir(() => Verbo(corredor, SnakeWay.Verbo, ""));
			AfirmarSw("W2. o ENMA tem \"Correr a Snake Way\" no menu: abaixo do minimo de BP recusa, o vivo recusa, e com o minimo a corrida COMECA",
					  noMenu && aoAlcance && !CorrendoASnakeWayDeTeste(fraco.Id) && recusaFraco.Any(s => s.Contains("Fraco demais"))
					  && !CorrendoASnakeWayDeTeste(visitante.Id) && recusaVivo.Any(s => s.Contains("caminho de morto"))
					  && CorrendoASnakeWayDeTeste(corredor.Id) && largada.Any(s => s.Contains("A PÉ")),
					  $"no menu {noMenu}, ao alcance {aoAlcance}; fraco: {string.Join(" | ", recusaFraco)}; vivo: {string.Join(" | ", recusaVivo)}; corredor: {string.Join(" | ", largada)}");

			// ---- W3) voar cancela
			ServerPlayer voador = Forjar(5, "bancada: o que voa", SnakeWay.BpMinimo * 2);
			MortoNaMesa(voador);
			Verbo(voador, SnakeWay.Verbo, "");
			bool comecou = CorrendoASnakeWayDeTeste(voador.Id);
			voador.Voando = true;
			List<string> voou = Ouvir(TickDaSnakeWay);
			voador.Voando = false;
			AfirmarSw("W3. VOAR CANCELA: quem voa no meio da corrida a perde, com o aviso",
					  comecou && !CorrendoASnakeWayDeTeste(voador.Id) && voou.Any(s => s.Contains("VOOU")), string.Join(" | ", voou));

			// ---- W4) (2026-10-27, o pedido 142) a chegada e o fim da estrada, a pe
			HashSet<(int X, int Y)> aPe = APeDoEnma();
			bool chegadaAPe = aPe.Any(c => SnakeWay.NaChegada(Caminho.Centro(c.X, c.Y)));
			bool kaiohAPe = aPe.Any(c => Math.Abs(c.X - NpcsDeConversa.SrKaiohX) <= 2 && Math.Abs(c.Y - NpcsDeConversa.SrKaiohY) <= 2);
			AfirmarSw("W4a. (o pedido 142) **NO MAPA DE VERDADE, DA MESA DO ENMA SE CHEGA A PE AO FIM DA ESTRADA** (a cabeca da serpente: sem parede -- a "
					  + "porta do palacio abre --, sem nuvem e sem agua) -- e ao Sr. Kaioh NAO (a chegada de antes: a ilha fica a 11 tiles de nuvem)",
					  chegadaAPe && !kaiohAPe, $"{aPe.Count} celula(s) a pe; a chegada {chegadaAPe}; o Sr. Kaioh {kaiohAPe}");

			double bpAntesDaChegada = corredor.Ficha.BP;
			MoveToZone(corredor.Id, alem, NaEstrada());
			List<string> noMeio = Ouvir(TickDaSnakeWay);
			bool seguiaNoMeio = CorrendoASnakeWayDeTeste(corredor.Id) && !corredor.Ficha.SnakeWayFeita;
			MoveToZone(corredor.Id, alem, NaCabeca());
			List<string> chegada = Ouvir(TickDaSnakeWay);
			AfirmarSw("W4b. A CHEGADA a pe no FIM DA ESTRADA fecha a corrida: +1.000 de BP uma vez e a marca, e o aviso manda voar ate o Sr. Kaioh "
					  + "(nove fileiras antes, na estrada, a corrida so segue)",
					  seguiaNoMeio && corredor.Ficha.SnakeWayFeita && !CorrendoASnakeWayDeTeste(corredor.Id)
					  && Math.Abs(corredor.Ficha.BP - bpAntesDaChegada - 1000) < 1e-6
					  && chegada.Any(s => s.Contains("correu a Snake Way") && s.Contains("voe até ele")),
					  $"no meio {seguiaNoMeio}, BP {bpAntesDaChegada:0} -> {corredor.Ficha.BP:0}; {string.Join(" | ", chegada)}");
			var opcoes = new JsonSerializerOptions { IncludeFields = true };
			Fighter? doSave = JsonSerializer.Deserialize<Fighter>(JsonSerializer.Serialize(corredor.Ficha, opcoes), opcoes);
			MoveToZone(corredor.Id, alem, AoLadoDoEnma());
			double bpAntesDaSegunda = corredor.Ficha.BP;
			List<string> segunda = Ouvir(() => Verbo(corredor, SnakeWay.Verbo, ""));
			AfirmarSw("W4c. ...a marca vai e volta do save (o JSON do Fighter), e a SEGUNDA corrida e recusada (o BP e uma vez na vida)",
					  doSave?.SnakeWayFeita == true && !CorrendoASnakeWayDeTeste(corredor.Id) && segunda.Any(s => s.Contains("já correu"))
					  && Math.Abs(corredor.Ficha.BP - bpAntesDaSegunda) < 1e-6,
					  $"do save {doSave?.SnakeWayFeita}; {string.Join(" | ", segunda)}");

			// ---- W5) o Sr. Kaioh so ensina a quem correu
			ServerPlayer aluno = Forjar(6, "bancada: o aluno do Kaioh", NpcsDeConversa.BpDoKaioken * 2);
			aluno.Karma = 100;
			aluno.Ficha.dead = true;
			aluno.MorteJaViajou = true;
			MoveToZone(aluno.Id, alem, AoLadoDoKaioh());
			List<string> semCorrer = Ouvir(() => SrKaiohFalar(aluno));
			bool aprendeuSem = aluno.Livro.Sabe(NpcsDeConversa.SkillDoKaioken);
			aluno.Ficha.SnakeWayFeita = true;
			List<string> correu = Ouvir(() => SrKaiohFalar(aluno));
			bool aprendeuCom = aluno.Livro.Sabe(NpcsDeConversa.SkillDoKaioken);
			AfirmarSw("W5. o SR. KAIOH so ensina a quem CORREU a Snake Way: sem a marca nao ensina o Kaio-ken (e diz por que); com ela, ensina",
					  !aprendeuSem && semCorrer.Any(s => s.Contains("SNAKE WAY")) && aprendeuCom,
					  $"sem: {aprendeuSem} ({string.Join(" | ", semCorrer)}); com: {aprendeuCom} ({string.Join(" | ", correu)})");
		}
		catch (Exception e) { AfirmarSw("a bancada rodou ate o fim", false, e.ToString()); }
		finally
		{
			foreach (ServerPlayer p in forjados)
			{
				_correndoASnakeWay.Remove(p.Id);
				_players.Remove(p.Id);
				ZoneList(p.Zone.Hash).Remove(p);
			}
		}
		GD.Print($"[snakeway] ================ {_swOk} passaram, {_swFalhou} falharam ================");
	}
}
