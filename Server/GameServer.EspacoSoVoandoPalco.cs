using Jandirus.Core.Items;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO `--diagespacovoo` (`Client/RoboDoEspacoSoVoando.cs`, 2026-10-20): as portas que o robo de cliente usa pra levar o
/// corpo dele ao vazio e traze-lo de volta, o pod do leme, e o que o servidor deste processo diz do corpo. Tudo pelos funis de producao
/// (`MoveToZone`, a tecla E do `nave_usar`, `Desembarcar`); o passo, o voo e as teclas sao do robo, pelo cliente de verdade. Uma troca de
/// zona por porta: duas no mesmo quadro poriam o cliente a carregar dois lugares de uma vez.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O pod do robo, enquanto existe -- a limpeza o tira do mundo.</summary>
	private Nave? _podDoPalcoEv;

	/// <summary>
	/// LEVA O CORPO AO VAZIO logo acima da Terra pelo `MoveToZone` -- o mesmo da subida (a pe: ele apaga o voo de quem troca de zona;
	/// ao leme, o pod vai junto) --, com a Roupa Espacial na mochila e o Ki cheio: o tique do vacuo roda de verdade neste processo, e sem
	/// o traje o robo morreria sufocado no meio da medida.
	/// </summary>
	public bool PalcoEv_AoEspaco(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (pl.Mochila.Quantos(CatalogoDeItens.Traje) == 0) pl.Mochila.Guardar(CatalogoDeItens.Traje);
		pl.Ficha.Ki = Math.Max(pl.Ficha.Ki, pl.Ficha.MaxKi);
		MoveToZone(pl.Id, ZonaDoEspaco, AcimaDaTerraEv());
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		MandarVizinhanca(pl);
		return Espaco.EhEspaco(pl.Zone);
	}

	/// <summary>De volta a Terra, no berco.</summary>
	public void PalcoEv_ATerra(int id)
	{
		ZoneKey terra = ZoneKey.Premade("Earth");
		if (_players.TryGetValue(id, out ServerPlayer? pl) && !pl.Zone.Equals(terra)) MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
	}

	/// <summary>Um pod parado aos pes do corpo, onde ele estiver, e a tecla E o embarca (`nave_usar`). Devolve se ficou ao leme dele.</summary>
	public bool PalcoEv_EmbarcarNoPod(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		_podDoPalcoEv ??= new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", DonoConta = pl.Conta, DonoNome = pl.Name, ErguidaEm = NowMs() };
		_podDoPalcoEv.PorZona(pl.Zone);
		_podDoPalcoEv.X = pl.Pos.X;
		_podDoPalcoEv.Y = pl.Pos.Y;
		if (!_naves.Contains(_podDoPalcoEv)) _naves.Add(_podDoPalcoEv);
		ComandoDeInteracao(pl, "nave_usar", "");
		return NaveDoPiloto(pl) == _podDoPalcoEv;
	}

	/// <summary>Sai do pod (onde estiver) e tira o pod do mundo.</summary>
	public void PalcoEv_SemPod(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && NaveDoPiloto(pl) is { } n) Desembarcar(pl, n);
		if (_podDoPalcoEv == null) return;
		_naves.Remove(_podDoPalcoEv);
		_podDoPalcoEv = null;
		GravarNaves();
	}

	/// <summary>
	/// O VOO DAS COBAIAS QUE VIAJAM NO VAZIO. No espaco so se anda voando (`ParadoNoEspaco`), e os robos que vao de encontro a um disco
	/// (o pouso da `--diagcachezona` e da `--diaggodki`, a cena do espaco do trailer) chegam la a pe -- o `MoveToZone` apaga o voo, e o
	/// atalho `decolar` das bancadas nao sobe voando -- e com corpos que nunca aprenderam a voar. A porta e a do jogador: a maestria de Ki
	/// que abre o voo (a mesma concessao do `--vooteste`), o Ki cheio pra decolar, e o `AlternarVoo` de producao. Devolve se o corpo voa.
	/// </summary>
	internal bool LigarOVooDaCobaiaNoTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (!PodeVoar(pl))
		{
			pl.Livro.Dar(SkillDoKi);
			pl.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
		}
		pl.Ficha.Ki = Math.Max(pl.Ficha.Ki, pl.Ficha.MaxKi);
		if (!pl.Voando) AlternarVoo(pl);
		return pl.Voando;
	}

	/// <summary>O que o servidor diz do corpo agora: no vazio? voando? a que altura? ao leme? o funil deixa andar?</summary>
	public (bool NoEspaco, bool Voando, float Altura, bool AoLeme, bool PodeAndar) PalcoEv_Estado(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? (Espaco.EhEspaco(pl.Zone), pl.Voando, pl.Altitude, EstaPilotando(pl.Id), PodeMexerOCorpo(pl))
			: (false, false, 0f, false, false);
}
