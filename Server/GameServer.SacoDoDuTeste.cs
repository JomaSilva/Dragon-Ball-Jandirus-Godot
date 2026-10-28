using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--sacoduteste` (2026-10-27) -- o pedido 111 do dono: *"veja como era o ganho do saco no DU e faça ele aqui"*. Roda no BOOT, com
/// corpos de PESSOA forjados num corredor livre e um saco de pancada no chao, e soca pela porta de producao (`Atacar`, o mesmo do pacote):
///   D1  o soco no saco da frente RENDE o ganho de luta, e na proporcao do multiplicador racial de BP (`BPMod` 2 rende o dobro de 1);
///   D2  custa 15 de Ki por soco;
///   D3  um soco a cada 1,5 s: o segundo logo em seguida nao rende nem gasta; passado o prazo, rende;
///   D4  sem Ki pro soco, nao rende;
///   D5  de costas pro saco (o saco atras), o soco vai pro ar e nao rende;
///   D6  o Majin nao usa o saco (como no DU).
///
///     Godot --headless --path . --server --port 11781 --sacoduteste
/// </summary>
public sealed partial class GameServer
{
	private int _sdOk, _sdFalhou;
	private const int IdBaseDoSacoDeTeste = 94_200;

	private void AfirmarSd(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _sdOk++; GD.Print($"[sacodu]   OK    {oque}   [{detalhe}]"); return; }
		_sdFalhou++;
		GD.PrintErr($"[sacodu]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoSacoDoDu()
	{
		_sdOk = _sdFalhou = 0;
		GD.Print("[sacodu] ================ O SACO DE PANCADA DO DU (dono, 2026-10-27) ================");

		// O CHAO DE VERDADE: os corpos e os sacos num corredor livre do mapa da bancada de projetil (o mesmo da `--ganhoteste`) -- sem o
		// mapa, o `CorredorLivre` devolveria o mesmo ponto pros tres, e o soco de um acertaria o corpo do outro.
		ZoneKey zona = ZonaDaBancadaDeProjetil;
		_pjMapa = MapaDaZonaOuCatalogo(zona);
		var corpos = new List<ServerPlayer>();
		var sacos = new List<Obra>();
		const double Bp = 50_000;
		ServerPlayer Forjar(int i, string nome, string raca, Vec2 onde)
		{
			var p = new ServerPlayer
			{
				Id = IdBaseDoSacoDeTeste + i, Peer = null, Name = nome, Race = raca, Genero = "Male", Idade = 25, Zone = zona,
				Pos = onde, Conta = $"bancada_sacodu_{i}", Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = raca, BP = Bp }, Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			p.Ficha.Class = "Normal";
			PorNoMundo(p);
			corpos.Add(p);
			// o saco no tile a LESTE dele
			var saco = new Obra { Id = 990_900 + i, Tipo = "Punching_Bag", X = onde.X + ZoneCollision.TileSize, Y = onde.Y, DonoNome = nome, Aparafusada = true };
			saco.PorZona(zona);
			_noChao.Add(saco);
			sacos.Add(saco);
			return p;
		}

		// UM SOCO de producao com a ficha arrumada como a da `--ganhoteste`, virado pra `rumo`: o BP e o Ki que ele mexeu
		(double Ganho, double Ki) Socar(ServerPlayer p, Facing rumo, double ki = -1)
		{
			PrepararParaMedir(p, Bp);
			if (ki >= 0) p.Ficha.Ki = ki;
			p.Facing = rumo;
			double kiAntes = p.Ficha.Ki;
			Atacar(p, Protocol.Golpe.Leve);
			return (p.Ficha.BP - Bp, kiAntes - p.Ficha.Ki);
		}

		try
		{
			ServerPlayer humano = Forjar(1, "bancada: o humano do saco", "Human", CorredorLivre(1));
			ServerPlayer dobrado = Forjar(2, "bancada: o do BPMod 2", "Human", CorredorLivre(3));
			ServerPlayer majin = Forjar(3, "bancada: o majin do saco", "Majin", CorredorLivre(5));

			// O KI DO SOCO COMUM (o golpe sempre custa um pouco): medido num soco pro ar, de costas pro saco -- e o que o saco soma por cima
			humano.UltimoSocoNoSaco = NowMs();
			(_, double kiDoSoco) = Socar(humano, Facing.West);
			humano.UltimoSocoNoSaco = NowMs() - MsEntreSocosNoSaco - 1;

			// ---- D1 e D2) o soco no saco rende, na proporcao do BPMod, e custa 15 de Ki
			humano.Ficha.BPMod = 1;
			dobrado.Ficha.BPMod = 2;
			(double ganho1, double ki1) = Socar(humano, Facing.East);
			(double ganho2, _) = Socar(dobrado, Facing.East);
			AfirmarSd("D1. o soco no SACO da frente rende o ganho de luta, na proporcao do BPMod (2 rende o dobro de 1)",
					  ganho1 > 0 && Math.Abs(ganho2 / ganho1 - 2) < 0.05, $"BPMod 1: +{ganho1:0.####}; BPMod 2: +{ganho2:0.####}");
			AfirmarSd("D2. ...e custa 15 de Ki por soco (por cima do Ki do soco comum)", Math.Abs(ki1 - kiDoSoco - KiDoSocoNoSaco) < 1e-6,
					  $"{ki1:0.##} de Ki, {kiDoSoco:0.##} do soco comum");

			// ---- D3) a cadencia de 1,5 s
			(double logoDepois, double kiLogoDepois) = Socar(humano, Facing.East);
			humano.UltimoSocoNoSaco = NowMs() - MsEntreSocosNoSaco - 1;
			(double passado, _) = Socar(humano, Facing.East);
			AfirmarSd("D3. um soco no saco a cada 1,5 s: o seguinte, na hora, nao rende nem gasta Ki; passado o prazo, rende",
					  logoDepois == 0 && Math.Abs(kiLogoDepois - kiDoSoco) < 1e-6 && passado > 0,
					  $"na hora +{logoDepois:0.####} ({kiLogoDepois:0.##} Ki, o do soco comum e {kiDoSoco:0.##}); depois +{passado:0.####}");

			// ---- D4) sem Ki
			humano.UltimoSocoNoSaco = NowMs() - MsEntreSocosNoSaco - 1;
			(double semKi, _) = Socar(humano, Facing.East, ki: KiDoSocoNoSaco - 1);
			AfirmarSd("D4. sem os 15 de Ki, o soco no saco nao rende", semKi == 0, $"+{semKi:0.####}");

			// ---- D5) de costas pro saco
			humano.UltimoSocoNoSaco = NowMs() - MsEntreSocosNoSaco - 1;
			(double deCostas, double kiDeCostas) = Socar(humano, Facing.West);
			AfirmarSd("D5. de costas pro saco o soco vai pro ar: nao rende nem gasta o Ki do saco (so o do soco comum)",
					  deCostas == 0 && Math.Abs(kiDeCostas - kiDoSoco) < 1e-6, $"+{deCostas:0.####}, {kiDeCostas:0.##} Ki");

			// ---- D6) o majin
			(double doMajin, _) = Socar(majin, Facing.East);
			AfirmarSd("D6. o MAJIN nao usa o saco (como no DU)", doMajin == 0, $"+{doMajin:0.####}");
		}
		finally
		{
			foreach (Obra s in sacos) _noChao.Remove(s);
			foreach (ServerPlayer p in corpos)
			{
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
		}
		GD.Print($"[sacodu] ================ {_sdOk} passaram, {_sdFalhou} falharam ================");
	}
}
