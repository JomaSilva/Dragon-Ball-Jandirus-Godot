using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O POMAR DOS MUNDOS SORTEADOS, DO LADO DO SERVIDOR -- as macieiras que todo planeta gerado tem.
///
/// O dono (2026-10-10): *"para facilitar a vida em planetas procedurais, esses planetas sempre devem ter uma
/// arvore frutifera (maça) pro player nunca morrer de fome"*.
///
/// ============================ E MOBILIA DE MAPA, COMO A DA TERRA ============================
/// As CELULAS saem da seed (<see cref="Pomar"/>, no Core: funcao pura do terreno). Aqui cada uma vira uma
/// `Obra` do tipo macieira com `DoMapa` -- exatamente o que o `.objetos` faz com as macieiras dos mapas
/// desenhados a mao (`PorMobiliaDoMapa`). Por isso NADA do resto foi escrito: o pacote de construcoes a
/// desenha, a camada de obras barra o corpo, a tecla E oferece "colher" e o `Colher` de sempre entrega a
/// maca, com os mesmos dez frutos e o mesmo broto (`GameServer.Interacao.cs`).
///
/// O QUE O `DoMapa` QUER DIZER PRA ELA, e sao as tres coisas certas:
///   * NAO VAI PRO `mundo.json` -- ela renasce da seed toda vez que o mundo nasce, como a da Terra renasce
///     do arquivo a cada boot. Gravar as duas daria uma macieira a mais por reinicio;
///   * NINGUEM A LEVA na mochila ("isto faz parte do lugar");
///   * CAI NO PRIMEIRO SOCO, como toda mobilia de mapa -- e e por isso que ela REBROTA (ver a ronda).
///
/// A MACIEIRA QUE ALGUEM PLANTA COM A SEMENTE continua sendo outra coisa: tem dono, vai pro disco e nao
/// rebrota. Se ela estiver na celula do pomar, o pomar a conta como a arvore dele e nao poe outra por cima.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>O pomar de UM mundo vivo: onde as macieiras ficam, e desde quando cada uma esta faltando.</summary>
	private sealed class PomarDaZona
	{
		public required ZoneKey Zona { get; init; }

		/// <summary>As celulas que a seed escolheu (<see cref="TerrenoGerado.Macieiras"/>). A da chegada e a primeira.</summary>
		public required IReadOnlyList<(int X, int Y)> Celulas { get; init; }

		/// <summary>Celula -> quando a ronda a viu SEM arvore (`NowMs`). Sem entrada = esta de pe.</summary>
		public readonly Dictionary<(int X, int Y), long> SemArvoreDesde = [];
	}

	/// <summary>
	/// Os pomares dos mundos gerados VIVOS, pela zona (<see cref="ZoneKey.Hash"/>). So na memoria: e do
	/// tamanho do cache de mundos (`_zonasGeradas`), e sai junto com o mundo (<see cref="ArrancarPomar"/>).
	/// </summary>
	private readonly Dictionary<ulong, PomarDaZona> _pomares = [];

	private long _proximaRondaDoPomar;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--pomarteste`): a macieira do pomar que cai NAO rebrota -- so volta quando o
	/// mundo nasce de novo. Lido pela propria regra, em <see cref="ReporMacieiras"/>. Falso em jogo, sempre.
	/// </summary>
	internal bool PomarNaoRebrotaDeTeste;

	/// <summary>
	/// O MUNDO ACABOU DE NASCER: guarda o pomar dele e poe as macieiras de pe. Chamado pela colheita das
	/// geracoes (`TickDasGeracoes`), antes de qualquer corpo pousar.
	///
	/// SO O QUE FALTA: um mundo que renasce no mesmo processo (saiu do cache e alguem voltou) pode achar na
	/// celula a macieira que um jogador plantou la, ou a propria arvore, se quem descarregou o mundo nao
	/// passou pelo <see cref="ArrancarPomar"/>.
	/// </summary>
	private void PlantarPomar(ZoneKey zona, IReadOnlyList<(int X, int Y)> celulas)
	{
		if (celulas.Count == 0) { _pomares.Remove(zona.Hash); return; }

		var pomar = new PomarDaZona { Zona = zona, Celulas = celulas };
		_pomares[zona.Hash] = pomar;

		int postas = ReporMacieiras(pomar, NowMs(), jaRebrotou: true);
		if (postas > 0) MandarObras(zona);
		GD.Print($"[server] pomar de {zona.Name}: {postas} macieira(s) de pe ({celulas.Count} na seed)");
	}

	/// <summary>
	/// O MUNDO SAIU DA MEMORIA: as macieiras do pomar saem com ele. Sem isto cada planeta visitado deixaria
	/// dezenas de obras pra sempre numa lista que o servidor varre por soco, por tecla E e por tique.
	///
	/// SO AS DO POMAR: a macieira plantada por alguem (`!DoMapa`) e do `mundo.json` e fica.
	/// </summary>
	private void ArrancarPomar(ulong hash)
	{
		if (!_pomares.Remove(hash, out PomarDaZona? pomar)) return;

		var celulas = new HashSet<(int, int)>(pomar.Celulas);
		_noChao.RemoveAll(o =>
		{
			if (!o.DoMapa || o.Tipo != CatalogoDeItens.ArvoreDeMaca || !o.Zona.Equals(pomar.Zona)) return false;
			if (!celulas.Contains(CatalogoDeObras.Celula(o.X, o.Y))) return false;
			_macas.Remove(o.Id);   // a conta de frutos e por id de obra, e este id nao volta
			return true;
		});
	}

	/// <summary>
	/// A RONDA DOS POMARES: a macieira que caiu volta a nascer, e o pomar de um mundo que ja nao esta na
	/// memoria e recolhido. Uma passada a cada <see cref="Pomar.SegundosEntreRondas"/> s -- fora dela, o
	/// tique paga uma comparacao.
	/// </summary>
	private void TickDoPomar()
	{
		long agora = NowMs();
		if (agora < _proximaRondaDoPomar) return;
		_proximaRondaDoPomar = agora + Pomar.SegundosEntreRondas * 1000L;
		if (_pomares.Count == 0) return;

		List<ulong>? orfaos = null;
		foreach ((ulong hash, PomarDaZona pomar) in _pomares)
		{
			// O MUNDO FOI DESCARREGADO por outra porta que nao a poda (uma bancada, um wipe): sem mapa nao
			// ha onde plantar, e as arvores que ficaram na lista sao lixo.
			if (!_zonasGeradas.ContainsKey(hash)) { (orfaos ??= []).Add(hash); continue; }
			if (ZonaMorta(pomar.Zona)) continue;   // planeta destruido nao rebrota

			if (ReporMacieiras(pomar, agora, jaRebrotou: false) > 0) MandarObras(pomar.Zona);
		}

		if (orfaos != null)
			foreach (ulong hash in orfaos) ArrancarPomar(hash);
	}

	/// <summary>
	/// POE DE PE AS MACIEIRAS QUE FALTAM neste pomar. Devolve quantas pos; quem chama manda o pacote.
	///
	/// <paramref name="jaRebrotou"/> pula a espera: e o nascimento do mundo, onde nada "caiu". Na ronda, a
	/// celula vazia primeiro ganha a marca de quando foi vista assim, e so <see cref="Pomar.SegundosPraRebrotar"/>
	/// depois a arvore volta.
	///
	/// A CELULA OCUPADA ESPERA (<see cref="CanteiroLivre"/>): a parede de alguem, uma nave pousada ou um
	/// corpo em cima. A arvore e densa, e nascer ali prenderia o corpo ou atravessaria a obra -- a ronda
	/// seguinte tenta de novo.
	/// </summary>
	private int ReporMacieiras(PomarDaZona pomar, long agora, bool jaRebrotou)
	{
		if (MapaDaZonaOuCatalogo(pomar.Zona) is not { } mapa) return 0;

		// AS MACIEIRAS DE PE NESTA ZONA, por celula: UMA passada na lista de obras, e nao uma por celula do
		// pomar (um mundo de lado 1000 tem 65 delas).
		HashSet<(int, int)> dePe = [];
		foreach (Obra o in _noChao)
			if (o.Tipo == CatalogoDeItens.ArvoreDeMaca && o.Zona.Equals(pomar.Zona))
				dePe.Add(CatalogoDeObras.Celula(o.X, o.Y));

		int postas = 0;
		foreach ((int cx, int cy) in pomar.Celulas)
		{
			if (dePe.Contains((cx, cy))) { pomar.SemArvoreDesde.Remove((cx, cy)); continue; }

			if (!jaRebrotou)
			{
				if (!pomar.SemArvoreDesde.TryGetValue((cx, cy), out long desde))
				{
					pomar.SemArvoreDesde[(cx, cy)] = agora;
					continue;
				}
				if (agora - desde < Pomar.SegundosPraRebrotar * 1000L || PomarNaoRebrotaDeTeste) continue;
			}

			if (!CanteiroLivre(pomar.Zona, mapa, cx, cy)) continue;

			PorMobiliaDePe(pomar.Zona, CatalogoDeItens.ArvoreDeMaca, cx, cy);
			pomar.SemArvoreDesde.Remove((cx, cy));
			postas++;
		}
		return postas;
	}

	/// <summary>
	/// ESTA CELULA DO POMAR ESTA LIVRE AGORA? O chao e da seed e ja foi escolhido firme; o que muda em jogo
	/// e o que alguem pos em cima.
	///
	/// AS PERGUNTAS VAO AS LISTAS, e nao so ao mapa: o mapa de um mundo que acabou de nascer ainda nao tem
	/// as camadas de obra e de base escritas (quem as escreve e a chegada do primeiro corpo, `MandarObras` e
	/// `MandarBlocos`), e o pomar e plantado antes disso.
	/// </summary>
	private bool CanteiroLivre(ZoneKey zona, ZoneCollision mapa, int cx, int cy)
	{
		if (!mapa.ServeDeChao(cx, cy)) return false;   // parede, beirada, agua ou nuvem
		if (ObraNaCelula(zona, cx, cy) != null || NaveNaCelula(zona, cx, cy) != null) return false;
		if (BlocoQueBarra(zona, cx, cy) != null) return false;

		if (_zones.TryGetValue(zona.Hash, out List<ServerPlayer>? gente))
			foreach (ServerPlayer corpo in gente)
				if (RegrasDeBloco.CorpoNaCelula(corpo.Pos, cx, cy)) return false;
		return true;
	}
}
