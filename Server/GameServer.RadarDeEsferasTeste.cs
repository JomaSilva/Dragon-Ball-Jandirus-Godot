using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO DISCO DO DRAGON RADAR (`--radarteste`) -- o que o servidor conta, e a quem.
///
/// O que se le e o FIO: os bytes do `S2C.RadarDeEsferas` que sairam pra conexao do testador
/// (<see cref="EscutaDoRadar"/>), desmontados pelo leitor de PRODUCAO (`FioDoRadarDeEsferas.Ler`, o mesmo do
/// `GameClient`). "O servidor decidiu mandar" e "chegou na tela" sao a mesma coisa aqui.
///
///   1. SEM o Dragon Radar o servidor nao manda NADA -- nem um pacote vazio;
///   2. COM ele, um pacote com as sete ACORDADAS deste mundo, nas posicoes delas; e o segundo tique, sem
///      nada ter mudado, nao manda nada;
///   3. esfera APAGADA nao vira sinal (o disco fica ligado, e vazio);
///   4. esfera de OUTRO mundo nao vira sinal;
///   5. do fio ao disco: o sinal cai no quadrante certo (norte em cima), e alem do alcance fica preso na borda;
///   6. esfera na mao anda com quem a carrega (por tile), e a que esta na MINHA mao vem marcada -- e o verb de
///      radar mede a esfera carregada do mesmo lugar que o disco;
///   7. perdeu o radar: UM pacote "desligado", e depois silencio;
///   8. trocar de mundo com o radar manda o disco do mundo novo;
///   9. o verb de radar e o disco contam as mesmas esferas.
///
/// Cada regra tem o defeito dela ligado no meio (`Radar...DeTeste`), e a checagem tem de reprovar com ele.
///
/// RODA NO PRIMEIRO TIQUE DE SEGUNDO DEPOIS DE ALGUEM ENTRAR (ela precisa de um corpo com conexao: e pra
/// conexao que o pacote sai), e devolve o mundo no fim -- sets, esferas, a mochila, a raca e o trono
/// emprestados, e o lugar do testador.
///
///     --headless --host --rede 8230 --radarteste --raca Human --conta bancada_radar --nome Rastreador
/// </summary>
public sealed partial class GameServer
{
	private static readonly bool RadarDeTeste = Array.IndexOf(OS.GetCmdlineArgs(), "--radarteste") >= 0;
	private bool _radarTestou;

	private void RodarBancadaDoRadar(ServerPlayer pl)
	{
		GD.Print("\n[radar] ===== BANCADA DO DISCO DO DRAGON RADAR =====");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[radar]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[radar]   FALHA {nome}   [{detalhe}]"); }
		}

		// A mesma checagem com o defeito ligado: tem de REPROVAR, e voltar a passar com ele desligado.
		void ComDefeito(string defeito, Action<bool> liga, Func<bool> checagem)
		{
			bool reprovou;
			liga(true);
			try { reprovou = !checagem(); }
			finally { liga(false); }
			Checa($"(defeito injetado: {defeito}) a checagem REPROVA, e volta a passar sem ele", reprovou && checagem());
		}

		var setsGuardados = new List<SetDeEsferas>(_sets);
		var esferasGuardadas = new List<Esfera>(_esferas);
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		string racaGuardada = pl.Race, classeGuardada = pl.Class;
		string tronoGuardado = _tronos.GetValueOrDefault("guardian", "");
		int radaresGuardados = pl.Mochila.Quantos(CatalogoDeItens.Radar);
		const int idDoOutro = 2_000_000_123;

		EscutaDoRadar = [];
		try
		{
			// ---------------------------------------------------------------- o palco
			_sets.RemoveAll(s => !s.Eterno);
			_esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
			var terra = ZoneKey.Premade("Earth");
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			pl.Race = "Namekian";
			pl.Class = "Dragon clan";
			(pl.Livro ??= new Jandirus.Core.Skills.SkillBook()).Dar(Jandirus.Core.Skills.AcendedoresDoPort.EstatuaDoDragao);   // (2026-10-13) a estatua pede a skill dela -- ver `ErguerEstatua`
			_tronos["guardian"] = pl.Conta;
			ErguerEstatua(pl, "");

			SetDeEsferas? set = _sets.Find(s => !s.Eterno && s.Zona.Hash == terra.Hash);
			List<Esfera> sete = set == null ? [] : [.. _esferas.Where(e => e.Set == set.Id).OrderBy(e => e.Numero)];
			Checa("(palco) o verbo de producao ergueu a estatua na Terra e as sete nasceram", set != null && sete.Count == Esferas.Total, $"{sete.Count} esferas");
			if (set == null || sete.Count != Esferas.Total) return;
			set.AtivoEm = TempoDoMundo - 1;
			pl.Mochila.Tirar(CatalogoDeItens.Radar, 99);

			// O QUE SAIU PRO TESTADOR NUM TIQUE, ja desmontado pelo leitor de producao.
			List<(bool Ligado, List<SinalDeEsfera> Sinais)> Tique()
			{
				EscutaDoRadar!.Clear();
				TickDoRadarDeEsferas();
				return [.. EscutaDoRadar.Where(x => x.Para == pl.Id).Select(x =>
				{
					var r = new NetDataReader(x.Fio);
					r.GetByte();   // o opcode
					return FioDoRadarDeEsferas.Ler(r);
				})];
			}
			static string Resumo(List<(bool Ligado, List<SinalDeEsfera> Sinais)> p) =>
				p.Count == 0 ? "nenhum pacote" : string.Join(" + ", p.Select(x => $"{(x.Ligado ? "ligado" : "DESLIGADO")} com {x.Sinais.Count} sinal(is)"));

			// ---------------------------------------------------------------- 1. sem o radar
			bool SemRadarNadaSai()
			{
				_radarMandado.Clear();
				return Tique().Count == 0 && Tique().Count == 0;
			}
			Checa("SEM o Dragon Radar na mochila o servidor nao manda nada (dois tiques, zero pacotes)", SemRadarNadaSai());
			ComDefeito("o servidor manda os sinais a quem nao tem o radar", v => RadarSemPortaoDeTeste = v, SemRadarNadaSai);

			// ---------------------------------------------------------------- 2. com o radar
			pl.Mochila.Guardar(CatalogoDeItens.Radar);
			_radarMandado.Clear();
			List<(bool Ligado, List<SinalDeEsfera> Sinais)> primeiro = Tique();
			Checa("COM o radar sai UM pacote, ligado, com as sete ACORDADAS deste mundo",
				  primeiro.Count == 1 && primeiro[0].Ligado && primeiro[0].Sinais.Count == Esferas.Total, Resumo(primeiro));
			bool nasPosicoes = primeiro.Count == 1 && sete.All(e => primeiro[0].Sinais.Any(s => s.X.Equals(e.X) && s.Y.Equals(e.Y) && !s.Carregada && !s.Comigo));
			Checa("...cada sinal na posicao EXATA de uma esfera do chao, sem marca de carregada", nasPosicoes);

			bool SoQuandoMuda()
			{
				_radarMandado.Clear();
				return Tique().Count == 1 && Tique().Count == 0 && Tique().Count == 0;
			}
			Checa("nada mudou: o segundo e o terceiro tiques nao mandam nada (estado comparado, e nao pulso)", SoQuandoMuda());
			ComDefeito("o pacote sai todo segundo, mudando ou nao", v => RadarSemComparaDeTeste = v, SoQuandoMuda);

			// ---------------------------------------------------------------- 3. apagadas
			bool ApagadaNaoAparece()
			{
				_radarMandado.Clear();
				set.AtivoEm = TempoDoMundo + 99999;
				try
				{
					List<(bool Ligado, List<SinalDeEsfera> Sinais)> p = Tique();
					return p.Count == 1 && p[0].Ligado && p[0].Sinais.Count == 0;
				}
				finally { set.AtivoEm = TempoDoMundo - 1; }
			}
			Checa("set APAGADO (ainda se refaz): o disco continua ligado e VAZIO -- nenhuma esfera apagada vira sinal", ApagadaNaoAparece());
			ComDefeito("esfera apagada tambem vira sinal", v => RadarVeApagadaDeTeste = v, ApagadaNaoAparece);

			// ---------------------------------------------------------------- 4. outro mundo
			var vegeta = ZoneKey.Premade("Vegeta");
			bool OutroMundoNaoAparece()
			{
				_radarMandado.Clear();
				sete[0].PorZona(vegeta);
				sete[1].PorZona(vegeta);
				try
				{
					List<(bool Ligado, List<SinalDeEsfera> Sinais)> p = Tique();
					return p.Count == 1 && p[0].Sinais.Count == Esferas.Total - 2;
				}
				finally { sete[0].PorZona(terra); sete[1].PorZona(terra); }
			}
			Checa("duas esferas levadas pra OUTRO mundo somem do disco daqui (o radar nao atravessa planeta)", OutroMundoNaoAparece());
			ComDefeito("esfera de outro mundo tambem vira sinal", v => RadarVeOutroMundoDeTeste = v, OutroMundoNaoAparece);

			// ---------------------------------------------------------------- 5. do fio ao disco
			const int T = ZoneCollision.TileSize;
			float alcance = RadarDeEsferas.AlcanceEmPx(RadarDeEsferas.AlcanceDeFabrica), meio = alcance / 2;
			(float Dx, float Dy)[] onde =
			[
				(meio, -meio), (-meio, -meio), (-meio, meio), (meio, meio),   // NE, NO, SO, SE
				(-alcance * 6, 0),                                            // muito alem do alcance, a oeste
				(0, 0),                                                       // em cima de mim
				(0, -alcance / 4),                                            // ao norte, perto
			];
			for (int i = 0; i < Esferas.Total; i++) { sete[i].X = pl.Pos.X + onde[i].Dx; sete[i].Y = pl.Pos.Y + onde[i].Dy; }

			bool NoQuadranteCerto()
			{
				_radarMandado.Clear();
				List<(bool Ligado, List<SinalDeEsfera> Sinais)> p = Tique();
				if (p.Count != 1 || p[0].Sinais.Count != Esferas.Total) return false;
				(float X, float Y, bool NaBorda) D(int i) => RadarDeEsferas.NoDisco(p[0].Sinais[i].X - pl.Pos.X, p[0].Sinais[i].Y - pl.Pos.Y, alcance);
				static bool Perto(float a, float b) => Math.Abs(a - b) < 0.01f;
				return Perto(D(0).X, 0.5f) && Perto(D(0).Y, -0.5f) && !D(0).NaBorda      // nordeste: direita e EM CIMA
					&& Perto(D(1).X, -0.5f) && Perto(D(1).Y, -0.5f)                     // noroeste
					&& Perto(D(2).X, -0.5f) && Perto(D(2).Y, 0.5f)                      // sudoeste
					&& Perto(D(3).X, 0.5f) && Perto(D(3).Y, 0.5f)                       // sudeste
					&& Perto(D(5).X, 0) && Perto(D(5).Y, 0) && !D(5).NaBorda            // em cima de mim: o centro
					&& Perto(D(6).X, 0) && Perto(D(6).Y, -0.25f);                       // ao norte: reto pra cima
			}
			Checa("do fio ao disco: quatro esferas nos quatro quadrantes caem cada uma no seu (NORTE EM CIMA), uma em cima de mim no centro",
				  NoQuadranteCerto());
			ComDefeito("o eixo Y do disco invertido", v => RadarDeEsferas.NorteParaBaixoDeTeste = v, NoQuadranteCerto);

			bool LongePresaNaBorda()
			{
				_radarMandado.Clear();
				List<(bool Ligado, List<SinalDeEsfera> Sinais)> p = Tique();
				if (p.Count != 1 || p[0].Sinais.Count != Esferas.Total) return false;
				(float x, float y, bool naBorda) = RadarDeEsferas.NoDisco(p[0].Sinais[4].X - pl.Pos.X, p[0].Sinais[4].Y - pl.Pos.Y, alcance);
				return naBorda && Math.Abs(x + 1f) < 0.001f && Math.Abs(y) < 0.001f;
			}
			Checa($"a esfera a {RadarDeEsferas.AlcancesEmTiles[RadarDeEsferas.AlcanceDeFabrica] * 6} tiles a oeste fica PRESA na borda oeste do disco", LongePresaNaBorda());
			ComDefeito("o sinal alem do alcance nao e preso", v => RadarDeEsferas.SemPrenderNaBordaDeTeste = v, LongePresaNaBorda);

			// ---------------------------------------------------------------- 6. na mao
			var outro = new ServerPlayer { Id = idDoOutro, Name = "Portador", Zone = pl.Zone, Pos = new Vec2(pl.Pos.X + 100 * T, pl.Pos.Y) };
			_players[idDoOutro] = outro;
			sete[6].Portador = pl.Id;
			sete[5].Portador = idDoOutro;
			_radarMandado.Clear();
			List<(bool Ligado, List<SinalDeEsfera> Sinais)> naMao = Tique();
			List<SinalDeEsfera> s6 = naMao.Count == 1 ? naMao[0].Sinais : [];
			Vec2 doOutro = RadarDeEsferas.PosicaoDaCarregada(outro.Pos), minha = RadarDeEsferas.PosicaoDaCarregada(pl.Pos);
			Checa("a esfera na MINHA mao vem marcada `Comigo` (uma so), no tile em que eu estou",
				  s6.Count(s => s.Comigo) == 1 && s6.Any(s => s.Comigo && s.Carregada && s.X.Equals(minha.X) && s.Y.Equals(minha.Y)), Resumo(naMao));
			Checa("a esfera na mao de OUTRO vale a posicao de QUEM A CARREGA (no centro do tile dele), marcada como carregada e nao como minha",
				  s6.Count(s => s.Carregada && !s.Comigo) == 1 && s6.Any(s => s.Carregada && !s.Comigo && s.X.Equals(doOutro.X) && s.Y.Equals(doOutro.Y)),
				  $"esperada {doOutro.X:0},{doOutro.Y:0}");
			Checa("...e as cinco do chao continuam sem marca", s6.Count(s => !s.Carregada) == Esferas.Total - 2);

			float nasceuEm = outro.Pos.X;
			outro.Pos = new Vec2(MathF.Floor(nasceuEm / T) * T + 3, outro.Pos.Y);
			TickDoRadarDeEsferas();
			outro.Pos = new Vec2(outro.Pos.X + 20, outro.Pos.Y);
			Checa("quem carrega andou DENTRO do mesmo tile: nenhum pacote (a posicao viaja por tile)", Tique().Count == 0);
			outro.Pos = new Vec2(outro.Pos.X + 3 * T, outro.Pos.Y);
			List<(bool Ligado, List<SinalDeEsfera> Sinais)> andou = Tique();
			Vec2 depois = RadarDeEsferas.PosicaoDaCarregada(outro.Pos);
			Checa("...andou tres tiles: UM pacote, com o sinal tres tiles adiante",
				  andou.Count == 1 && andou[0].Sinais.Any(s => s.Carregada && !s.Comigo && s.X.Equals(depois.X)) && depois.X - doOutro.X >= 2 * T, Resumo(andou));

			bool OVerbSegueQuemCarrega()
			{
				EscutaDeAvisos = [];
				try
				{
					UsarORadar(pl);
					string? linha = EscutaDeAvisos.Find(t => t.Contains("com Portador"));
					return linha != null && linha.Contains("leste") && linha.Contains($" {(outro.Pos.X - pl.Pos.X) / T:0} tiles");
				}
				finally { EscutaDeAvisos = null; }
			}
			Checa("o VERB de radar mede a esfera carregada de onde esta QUEM A CARREGA (a leste, a uma centena de tiles) -- o mesmo lugar que o disco mostra",
				  OVerbSegueQuemCarrega(), $"{(outro.Pos.X - pl.Pos.X) / T:0} tiles a leste");
			ComDefeito("o verb mede a esfera carregada do ponto em que ela foi pega", v => RadarMedeDeOndeFoiPegaDeTeste = v, OVerbSegueQuemCarrega);
			sete[6].Portador = 0;
			sete[5].Portador = 0;

			// ---------------------------------------------------------------- 9. o verb e o disco concordam
			_radarMandado.Clear();
			List<(bool Ligado, List<SinalDeEsfera> Sinais)> disco = Tique();
			EscutaDeAvisos = [];
			UsarORadar(pl);
			int noVerb = EscutaDeAvisos.Count(t => t.Contains("estrela"));
			EscutaDeAvisos = null;
			Checa("o verb de radar e o disco contam as MESMAS esferas", disco.Count == 1 && disco[0].Sinais.Count == noVerb && noVerb == Esferas.Total,
				  $"verb {noVerb}, disco {(disco.Count == 1 ? disco[0].Sinais.Count : -1)}");

			// ---------------------------------------------------------------- 8. outro mundo, com o radar
			MoveToZone(pl.Id, vegeta, PontoDeNascimento(vegeta));
			List<(bool Ligado, List<SinalDeEsfera> Sinais)> emVegeta = Tique();
			Checa("trocar de mundo com o radar manda o disco do mundo novo: ligado, e sem nenhuma esfera da Terra",
				  emVegeta.Count == 1 && emVegeta[0].Ligado && emVegeta[0].Sinais.Count == 0, Resumo(emVegeta));
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			Tique();

			// ---------------------------------------------------------------- 7. perdeu o radar
			bool PerdeuODiscoSome()
			{
				pl.Mochila.Guardar(CatalogoDeItens.Radar);
				_radarMandado.Clear();
				if (Tique().Count != 1) return false;
				pl.Mochila.Tirar(CatalogoDeItens.Radar, 99);
				List<(bool Ligado, List<SinalDeEsfera> Sinais)> p = Tique();
				return p.Count == 1 && !p[0].Ligado && p[0].Sinais.Count == 0 && Tique().Count == 0;
			}
			pl.Mochila.Tirar(CatalogoDeItens.Radar, 99);
			Checa("perdeu o radar: sai UM pacote DESLIGADO (o disco some), sem sinal nenhum, e depois silencio", PerdeuODiscoSome());
			ComDefeito("o servidor manda os sinais a quem nao tem o radar", v => RadarSemPortaoDeTeste = v, PerdeuODiscoSome);
		}
		finally
		{
			RadarSemPortaoDeTeste = RadarVeApagadaDeTeste = RadarVeOutroMundoDeTeste = RadarSemComparaDeTeste = RadarMedeDeOndeFoiPegaDeTeste = false;
			RadarDeEsferas.NorteParaBaixoDeTeste = RadarDeEsferas.SemPrenderNaBordaDeTeste = false;
			_players.Remove(idDoOutro);
			EscutaDoRadar = null;
			EscutaDeAvisos = null;

			_sets.Clear(); _sets.AddRange(setsGuardados);
			_esferas.Clear(); _esferas.AddRange(esferasGuardadas);
			_invocacoes.Clear();

			pl.Mochila.Tirar(CatalogoDeItens.Radar, 99);
			if (radaresGuardados > 0) pl.Mochila.Guardar(CatalogoDeItens.Radar, radaresGuardados);
			pl.Race = racaGuardada;
			pl.Class = classeGuardada;
			if (tronoGuardado.Length > 0) _tronos["guardian"] = tronoGuardado;
			else _tronos.Remove("guardian");
			MoveToZone(pl.Id, zonaGuardada, posGuardada);

			// O QUE A TELA DO TESTADOR RECEBEU NA BANCADA NAO VALE MAIS: o proximo tique de producao manda de novo.
			_radarMandado.Clear();
			SalvarEsferas();

			GD.Print($"[radar] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}
}
