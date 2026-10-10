using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DA FOTO DAS MAQUINAS (`--diagmaquinas`, `Client/RoboDasMaquinas.cs`).
///
/// SO MONTAM A CENA E LEEM O ESTADO. A maquina e assentada pelo `posicionar`, o alcance e comprado pelo
/// `grav_up`, a sala sobe pelo `ComandoDeBloco` -- os funis de producao, com o corpo local como autor. Quem
/// aparafusa, liga o tanque e ajusta a gravidade e a bancada, PELA TELA: o menu da tecla E e o teclado
/// numerico. Quem cura e quem pesa sao o pulso e o tique de producao. A regra que a `--maquinasteste` segue.
/// </summary>
public partial class GameServer
{
	/// <summary>O que esta bancada pos no mundo -- o <see cref="LimparAFotoDasMaquinas"/> tira.</summary>
	private readonly List<Obra> _fotoDasMaquinasObras = [];
	private readonly List<(ZoneKey Zona, int X, int Y)> _fotoDasMaquinasBlocos = [];

	/// <summary>
	/// O CORPO VAI PRO MEIO DE UM QUADRADO DE CHAO COMUM: <paramref name="folga"/> tiles livres pra cada lado
	/// -- sem parede, agua, predio do mapa, obra nem bloco. A sala da cena e a maquina precisam do quadrado
	/// inteiro, e nao so da cruz que a praca da boca do cano garante.
	/// </summary>
	internal bool AssentarNaPracaDasMaquinas(int id, int folga)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || MapaDaZonaOuCatalogo(pl.Zone) is not { } mapa) return false;

		const int T = ZoneCollision.TileSize;
		(int cx, int cy) = CatalogoDeObras.Celula(pl.Pos.X, pl.Pos.Y);
		for (int r = 0; r <= 40; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (r > 0 && Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // so a casca: o miolo ja foi visto
					if (!QuadradoDeChaoComum(pl.Zone, mapa, cx + dx, cy + dy, folga)) continue;

					CravarPosicao(pl, new Vec2((cx + dx) * T + T / 2f, (cy + dy) * T + T / 2f - MoveRules.FeetOffsetY));
					pl.Moving = false;
					return true;
				}
		return false;
	}

	private bool QuadradoDeChaoComum(ZoneKey zona, ZoneCollision mapa, int cx, int cy, int folga)
	{
		for (int y = cy - folga; y <= cy + folga; y++)
			for (int x = cx - folga; x <= cx + folga; x++)
				if (!mapa.ServeDeChao(x, y) || mapa.NasceuDentro(x, y) || mapa.Indestrutivel(x, y) || mapa.Selada(x, y)
					|| ObraNaCelula(zona, x, y) != null || BlocoEm(zona, x, y) != null)
					return false;
		return true;
	}

	/// <summary>
	/// UMA MAQUINA A (dx, dy) TILES DO DONO, assentada pelo funil de producao (`AssentarDeTeste`: mochila +
	/// `posicionar`). Nasce SOLTA, como toda maquina que alguem assenta -- o parafuso e do menu.
	/// </summary>
	/// <returns>o id da obra, ou 0 se o servidor recusou.</returns>
	internal int MaquinaDaFoto(int idDono, string tipo, int dx, int dy)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? pl)) return 0;
		(int cx, int cy) = CatalogoDeObras.Celula(pl.Pos.X, pl.Pos.Y);
		if (AssentarDeTeste(pl, tipo, cx + dx, cy + dy) is not { } obra) return 0;
		_fotoDasMaquinasObras.Add(obra);
		return obra.Id;
	}

	private Obra? ObraDaFotoDasMaquinas(int obraId)
	{
		foreach (Obra o in _fotoDasMaquinasObras)
			if (o.Id == obraId && _noChao.Contains(o)) return o;
		return null;
	}

	/// <summary>
	/// O TANQUE DA CENA COM A VELOCIDADE DO DA CIDADE (10: +15 de vida por pulso). Com a de fabrica a cura de
	/// um corpo ferido levaria um minuto de janela parada; o que a cena mostra e a mesma conta, mais depressa.
	/// </summary>
	internal void TurbinarOTanqueDaFoto(int obraId)
	{
		if (ObraDaFotoDasMaquinas(obraId) is not { } o) return;
		RegeneradorDaObra r = EstadoDoTanque(o);
		r.Eficiencia = Regenerador.EficienciaDaCidade;
		r.Energia = r.EnergiaMax = Regenerador.BateriaDaCidade;
	}

	internal (bool Existe, bool Aparafusado, bool Ligado, double Energia, double EnergiaMax, int Eficiencia) TanqueNaFoto(int obraId)
	{
		if (ObraDaFotoDasMaquinas(obraId) is not { } o) return default;
		RegeneradorDaObra r = EstadoDoTanque(o);
		return (true, o.Aparafusada, r.Ligado, r.Energia, r.EnergiaMax, r.Eficiencia);
	}

	/// <summary>
	/// FERE OS QUATRO MEMBROS (bracos e pernas a 30%). Nenhum vital abaixo de 70%, de proposito: e o ramo do
	/// pulso que cura TODO membro ferido, e nao o que para nos vitais.
	/// </summary>
	internal bool FerirNaFotoDasMaquinas(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate is not { } c) return false;
		foreach (string nome in (string[])["Braco esquerdo", "Braco direito", "Perna esquerda", "Perna direita"])
			if (c.Corpo.Achar(nome) is { Decepado: false } p) p.Vida = p.VidaMax * 0.30;
		c.SincronizarVida();
		pl.CorpoEnviado = "";
		return true;
	}

	/// <summary>
	/// O CORPO DA CENA JA TREINOU EM GRAVIDADE. Um personagem recem-criado tem maestria 1, e os 10x da maquina
	/// o esmagariam e o PRENDERIAM no chao (`Esmagamento.RazaoQuePrende`: gravidade sentida a partir de 4x a
	/// maestria) -- a cena e sobre o campo, e precisa de alguem que consiga sair andando dele.
	/// </summary>
	internal void MaestriaDeGravidadeNaFotoDasMaquinas(int id, double maestria)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.GravMastered = Math.Max(pl.Ficha.GravMastered, maestria);
		pl.Ficha.Statify();
		pl.SigAtributos = "";
	}

	/// <summary>A vida, o campo de maquina e a gravidade sentida deste corpo, na conta do SERVIDOR.</summary>
	internal (bool Existe, double Vida, double Campo, double Gravidade, Vec2 Pos, long NoTanqueAte) CorpoNaFotoDasMaquinas(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl)
			? (true, pl.Combate?.Corpo.Vida() ?? pl.Ficha.HP, pl.Ficha.gravmult, Jandirus.Core.Stats.Esmagamento.Gravidade(pl.Ficha), pl.Pos, pl.NoTanqueAte)
			: default;

	/// <summary>
	/// O ALCANCE DA MAQUINA DA CENA, comprado pelo verbo de producao (`grav_up alcance`) com o zeni que a
	/// cena entrega. Devolve o alcance em que ela ficou.
	/// </summary>
	internal int AlcanceNaFotoDasMaquinas(int idDono, int obraId, int ate)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? pl) || ObraDaFotoDasMaquinas(obraId) is not { } o) return -1;
		pl.Ficha.Zeni += 100_000;
		for (int i = 0; i < 12 && (o.Gravidade?.Range ?? 0) < ate; i++) ComandoDeInteracao(pl, "grav_up", "alcance");
		return o.Gravidade?.Range ?? 0;
	}

	internal (bool Existe, bool Aparafusada, double Grav, int Alcance, int Tiles, bool Mudando, double Pedida) CampoNaFoto(int obraId)
	{
		if (ObraDaFotoDasMaquinas(obraId) is not { } o) return default;
		GravidadeDaObra? g = o.Gravidade;
		return (true, o.Aparafusada, g?.Grav ?? 0, g?.Range ?? 0, g?.Cheias?.Count ?? 0, g is { MudaEm: > 0 }, g?.Pedida ?? 0);
	}

	/// <summary>
	/// A SALA DA CENA: um anel de parede de (x0, y0) a (x1, y1) com UMA porta sem senha, erguido pelo
	/// `ComandoDeBloco` com o dono como construtor (ele esta dentro, a menos de quatro tiles de cada bloco).
	/// Devolve quantos blocos subiram.
	/// </summary>
	internal int SalaNaFotoDasMaquinas(int idDono, int x0, int y0, int x1, int y1, int portaX, int portaY)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? pl)) return 0;
		if (Bloco(ClasseDeBloco.Parede) is not { } parede || Bloco(ClasseDeBloco.Porta) is not { } porta) return 0;

		int erguidos = 0;
		for (int y = y0; y <= y1; y++)
			for (int x = x0; x <= x1; x++)
			{
				if (x != x0 && x != x1 && y != y0 && y != y1) continue;
				ComandoDeBloco(pl, Protocol.BlocoErguer, x, y, (x, y) == (portaX, portaY) ? porta.Numero : parede.Numero, "");
				if (BlocoEm(pl.Zone, x, y) == null) continue;
				_fotoDasMaquinasBlocos.Add((pl.Zone, x, y));
				erguidos++;
			}
		return erguidos;
	}

	/// <summary>Tudo o que a cena pos no mundo sai: as maquinas, a sala -- e os defeitos injetados desligam.</summary>
	internal void LimparAFotoDasMaquinas()
	{
		CampoDeGravidade.AtravessaParedeDeTeste = CampoDeGravidade.SoMudaNaMaquinaDeTeste = false;
		Regenerador.DesligadoCuraDeTeste = Regenerador.VizinhoContaDeTeste = false;

		var zonas = new HashSet<ZoneKey>();
		foreach (Obra o in _fotoDasMaquinasObras)
			if (_noChao.Remove(o)) zonas.Add(o.Zona);
		_fotoDasMaquinasObras.Clear();

		foreach ((ZoneKey zona, int x, int y) in _fotoDasMaquinasBlocos)
		{
			if (!_bases.TryGetValue(zona.Hash, out BaseDaZona? z) || !z.Celulas.TryGetValue((x, y), out BlocoDePe? b)) continue;
			TirarBloco(z, b);
			AnunciarBlocoTirado(z, x, y, derrubado: false);
		}
		_fotoDasMaquinasBlocos.Clear();

		if (zonas.Count > 0) GravarMundo();
		foreach (ZoneKey zona in zonas) MandarObras(zona);
		LerOsCampos();
	}
}
