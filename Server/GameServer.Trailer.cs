using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Magic;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS JANELAS DO DIRETOR DO TRAILER (`--trailer`) ============================
/// O dono pediu um VIDEO do jogo. Video nao e foto: as bancadas de foto desta casa tiram um quadro e
/// param o mundo em volta dele (pausam a arvore, escondem o tiro, injetam o defeito de proposito), e
/// filmar isso da uma cena que engasga. O diretor (`Client/RoboDoTrailer.cs`) so ARRUMA O PALCO e deixa
/// o jogo rodar -- e o que ele precisa do servidor pra arrumar o palco mora aqui.
///
/// ============================ O QUE ESTAS JANELAS **NAO** FAZEM ============================
/// Nenhuma delas desenha, e nenhuma decide resultado de golpe, de tiro ou de forma: quem transforma e
/// o `AdminForcarForma`, quem atira e o `UsarHabilidade`, quem luta e o `Cerebro` de producao com a
/// presa imposta pelo MESMO campo que o torneio usa (`PapelDeNpc.PresaDoRoteiro`). O que elas escrevem
/// e o que um diretor de cena escreve: a hora, o tempo, o figurino e quem esta em cena.
///
/// E NADA DAQUI RODA SOZINHO: sem a flag `--trailer` no cliente ninguem chama este arquivo.
/// ==========================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O `lugar` dos NPCs de cena -- longe do povoamento, pra a semente nao repetir um cidadao.</summary>
	private ulong _lugarDoTrailer = 7_700_000;

	/// <summary>Os corpos que o diretor pos no mundo, pra o <see cref="LimparOTrailer"/> recolher.</summary>
	private readonly List<int> _nascidosDoTrailer = [];

	/// <summary>
	/// A HORA E O TEMPO DA ZONA EM QUE O JOGADOR ESTA -- os dois juntos, porque no video os dois sao
	/// a mesma coisa: luz.
	///
	/// A hora vai pela correcao MINIMA do `_adiantoDoCeu` (a mesma conta do `SolAPinoNaFotoDeFusao`, e
	/// pelo motivo escrito la: `AdminMeioDia` pula um dia inteiro a cada chamada). O clima e CANCELADO
	/// antes de ser forcado porque o `ForcarClima` deixa o mais forte vencer -- e um diretor que pede
	/// ceu limpo depois de uma nevasca de forca 1 ficaria com a nevasca.
	/// </summary>
	internal void CeuDoTrailer(int id, double hora, TipoDeClima clima, double forca = 1)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;

		// HORA NEGATIVA = "deixa o relogio onde esta": a cena da lua cheia chega a noite certa pelo
		// `admin_lua_cheia` e so quer o ceu aberto por cima dela.
		if (hora >= 0) _adiantoDoCeu += CorrecaoDaHora(pl, hora);

		ForcarClima(pl.Zone, TipoDeClima.Limpo, 0);
		ForcarClima(pl.Zone, clima, 3600, forca, "trailer");

		// O RELOGIO NOVO VAI JA, e nao no proximo tique do ceu (1 Hz): uma cena que comeca no escuro e
		// clareia um segundo depois e um corte perdido.
		foreach (ServerPlayer o in _players.Values) MandarCeu(o);
	}

	/// <summary>
	/// QUANTOS SEGUNDOS DE CEU FALTAM (ou sobram) pra a zona deste corpo marcar `hora`, pelo caminho
	/// MAIS CURTO -- inclusive pra tras e inclusive atravessando a meia-noite. O caminho curto e o que
	/// faz a correcao ser idempotente e nao andar a fase da lua: quem pede 0,02 as 0,98 quer avancar
	/// 0,04 de dia, e nao voltar 0,96.
	/// </summary>
	private double CorrecaoDaHora(ServerPlayer pl, double hora)
	{
		RelogioDoPlaneta relogio = RelogioDaZona(pl.Zone);
		double dia = relogio.DiaLocal(TempoDoMundo);
		double falta = hora - (dia - Math.Floor(dia));
		if (falta > 0.5) falta -= 1;
		else if (falta < -0.5) falta += 1;
		return falta * relogio.SegundosPorDia;
	}

	/// <summary>
	/// A HORA FICA CRAVADA -- so o relogio, sem tocar no tempo. O diretor chama isto duas vezes por
	/// segundo enquanto a cena roda.
	///
	/// ============================ POR QUE CRAVAR, E NAO ACERTAR UMA VEZ ============================
	/// Um dia deste jogo dura 24 minutos (`Ceu.SegundosPorDia`), e uma tomada dura tres ou quatro: o
	/// sol anda 15% do dia entre o primeiro plano e o ultimo. A tomada do arsenal comecou as 0,70 com
	/// o brilho medio da tela em 80 e acabou em 31 -- noite fechada --, e a escada do Super Saiyajin
	/// "ao anoitecer" passou os 143 s do SSJ3 no escuro. E a mesma licao do `SolAPinoNaFotoDeFusao`
	/// (a luz nao pode ser uma variavel), so que la sao fotos e aqui e video.
	/// ==============================================================================================
	/// </summary>
	internal void HoraDoTrailer(int id, double hora)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		double falta = CorrecaoDaHora(pl, hora);
		if (Math.Abs(falta) < 0.4) return;
		_adiantoDoCeu += falta;
		foreach (ServerPlayer o in _players.Values) MandarCeu(o);
	}

	/// <summary>Que horas sao agora na zona deste corpo (0 = meia-noite, 0,5 = meio-dia).</summary>
	internal double HoraAgoraNoTrailer(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl) ? CeuDe(pl).Hora : 0;

	/// <summary>
	/// O CEU ANDA `segundos` -- o dia passando depressa. O ceu e funcao pura do tempo, entao adiantar o
	/// tempo E a cena; nenhum pacote novo.
	/// </summary>
	internal void AndarOCeuDoTrailer(double segundos)
	{
		_adiantoDoCeu += segundos;
		foreach (ServerPlayer o in _players.Values) MandarCeu(o);
	}

	/// <summary>
	/// O FIGURINO: o penteado e o guarda-roupa INTEIRO de um corpo, trocados de uma vez.
	///
	/// O personagem de linha de comando nasce sem uma peca vestida (ver `VestirNaFotoDeFusao`), e o
	/// heroi de um trailer de calcao nao e o jogo que o dono fez. As pecas sao achadas pelo NOME do
	/// arquivo (`VisualCatalog.Peca`), e a que nao existir e DITA -- um figurino que some calado e a
	/// armadilha que aquele metodo ja documenta.
	/// </summary>
	/// <returns>os nomes que NAO foram achados no catalogo (vazio = vestiu tudo).</returns>
	internal List<string> VestirNoTrailer(int id, string cabelo, string[] pecas)
	{
		var faltou = new List<string>();
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return faltou;

		if (cabelo.Length > 0)
		{
			if (_visual?.TemCabeloChamado(cabelo) == true) { pl.Visual.Cabelo = cabelo; pl.Visual.CorCabelo = null; }
			else faltou.Add("cabelo " + cabelo);
		}

		// `Nome#rrggbb` TINGE a peca -- a mesma cor que a tela de criacao deixa escolher por peca
		// (`PecaDeRoupa.Cor`). Sem a tinta, as pecas desenhadas pra serem tingidas saem cinza.
		pl.Visual.Roupa.Clear();
		foreach (string pedido in pecas)
		{
			string[] par = pedido.Split('#', 2);
			if (_visual?.Peca(par[0].Trim()) is not { } caminho) { faltou.Add(par[0]); continue; }

			Rgb? cor = null;
			if (par.Length == 2 && par[1].Length == 6
				&& int.TryParse(par[1], System.Globalization.NumberStyles.HexNumber, null, out int hex))
				cor = new Rgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
			pl.Visual.Roupa.Add(new PecaDeRoupa(caminho, cor));
		}

		TrocarAparencias(pl);
		return faltou;
	}

	/// <summary>
	/// UM NPC DE VERDADE AO LADO DO JOGADOR -- pelo `NascerNpc` de producao, com cerebro, perfil e ficha
	/// sorteados do molde. Chefe entra no degrau pedido pelo mesmo par de linhas da saga e da lembranca
	/// (`Sagas.Pinar` + `EntrarNoDegrau`, ver `ChamarChefe`).
	/// </summary>
	/// <param name="bps">
	/// O BP de cada degrau do roteiro, quando a cena quer outro que nao o do molde. Vai pelo MESMO campo
	/// que a saga escreve (`BpsPinados`) -- e existe porque uma luta de trailer precisa DURAR: o
	/// guardiao de 3 milhoes contra o Cell de 150 e um soco so.
	/// </param>
	/// <returns>o id do corpo, ou 0 se o molde nao nasceu (o motivo sai no console, pelo `NascerNpc`).</returns>
	internal int NpcDoTrailer(int idDono, string moldeId, Vec2 desloc, int estagio = 0, double[]? bps = null)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return 0;

		ServerPlayer? npc = NascerNpc(moldeId, dono.Zone, dono.Pos + desloc, ++_lugarDoTrailer);
		if (npc == null) return 0;

		if (npc.Papel is { Molde.EhChefe: true } papel)
		{
			double[] pinados = Sagas.Pinar(papel.Molde, MediaDoServidor());
			if (bps != null)
				for (int i = 0; i < pinados.Length && i < bps.Length; i++) pinados[i] = bps[i];
			papel.BpsPinados = pinados;
			papel.Estagio = estagio;
			EntrarNoDegrau(npc, cura: 0, anunciar: false);
		}

		// LUTA DE CENA E LUTA COMPLETA: o sorteio do molde pode tirar o voo ou o ki de um corpo
		// (`PerfilDeCombate.Sortear`), e um duelo filmado em que um dos dois so anda nao mostra o jogo.
		npc.Perfil = Jandirus.Core.Ai.PerfilDeCombate.Completo;

		_nascidosDoTrailer.Add(npc.Id);
		return npc.Id;
	}

	/// <summary>
	/// O CHEFE SOBE UM DEGRAU DO ROTEIRO -- o `AvancarEstagio` de producao, com o anuncio, a cura e a
	/// troca de corpo que a transicao de verdade tem. O gatilho de producao e a vida do chefe cair ate
	/// o limiar do degrau (`TickDoRoteiro`); aqui o diretor escolhe a HORA, e nao o que acontece nela.
	/// </summary>
	internal bool DegrauNoTrailer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? npc) || npc.Papel is not { } papel) return false;
		if (papel.Estagio + 1 >= papel.Molde.Estagios.Length) return false;
		AvancarEstagio(npc);
		return true;
	}

	/// <summary>
	/// O PODER DE UM CORPO DE CENA, igualado ao que o diretor pede -- a mesma escrita do `--bpteste`
	/// (`BP` + `Statify`), mais o `PowerLevel` que um corpo sem dono so ganharia no proximo tique.
	/// Existe pra um duelo durar: dois lutadores sorteados com BP de 500 a 5000 acabam no primeiro soco.
	/// </summary>
	internal void PoderNoTrailer(int id, double bp)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.BP = bp;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Combate?.Corpo.Restaurar();
		pl.Combate?.SincronizarVida();
	}

	/// <summary>
	/// A PRESA IMPOSTA -- o `tourney_engage` do torneio (`Engajar`), pra dois corpos quaisquer. `b == 0`
	/// solta. Corpo sem papel (jogador, ator forjado) nao tem presa pra receber e fica como esta.
	/// </summary>
	internal void DueloNoTrailer(int a, int b)
	{
		_players.TryGetValue(a, out ServerPlayer? pa);
		_players.TryGetValue(b, out ServerPlayer? pb);
		if (pa?.Papel != null) pa.Papel.PresaDoRoteiro = pb?.Id ?? 0;
		if (pb?.Papel != null) pb.Papel.PresaDoRoteiro = pa?.Id ?? 0;
	}

	/// <summary>FORCA UMA FORMA NUM CORPO QUALQUER -- o `admin_forma`, com o Ki cheio (ver `FormaNaFotoDeFusao`).</summary>
	internal void FormaNoTrailer(int id, string forma)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AdminForcarForma(pl, forma);
	}

	/// <summary>
	/// APERTA A TECNICA DE UM CORPO QUALQUER, pelo `UsarHabilidade` de producao (o botao do jogador).
	/// Devolve o que o servidor respondeu -- ver `GatilhoDaVariedade`, que e esta mesma janela pro
	/// corpo do jogador.
	/// </summary>
	internal string TecnicaNoTrailer(int id, string verbo) => GatilhoDaVariedade(id, verbo);

	/// <summary>Onde este corpo esta e pra onde olha, na conta do servidor.</summary>
	internal (bool Existe, Vec2 Pos, Facing Olhar, bool KO, bool Morto, string Forma, string Nome) CorpoDoTrailer(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl)
			? (true, pl.Pos, pl.Facing, pl.Ficha.KO, pl.Ficha.dead, pl.Forma.Atual, pl.Name)
			: (false, default, default, false, false, "", "");

	/// <summary>A zona em que o jogador esta, pelo nome -- o diretor confere que a viagem chegou.</summary>
	internal string ZonaDoTrailer(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? pl.Zone.Name : "";

	/// <summary>
	/// ============================ AS SETE ESFERAS AOS PES DO JOGADOR, ACESAS ============================
	/// Reunir as sete e uma viagem de horas (elas se espalham por semente, `EspalharOSet`) e a espera de
	/// recarga e de dias. A cena do dragao so precisa do ULTIMO gesto -- as sete juntas, e o `db_invocar`
	/// de producao a partir dai --, entao a janela traz as esferas do set DESTA zona pra roda em volta
	/// de quem invoca e acorda o set.
	///
	/// SEM SET NA ZONA, ELA ERGUE UM, e isto e a unica coisa daqui que nao passa pela porta de producao
	/// (`ErguerEstatua`): la a estatua da Terra exige um Namekuseijin do Cla do Dragao que seja o
	/// Guardiao, e montar essa ficha inteira pra filmar o Shenron seria escrever tres sistemas a mao.
	/// Os campos sao os que aquela porta escreve fora de Namek, copiados de la.
	/// ==================================================================================================
	/// </summary>
	/// <returns>o nome do dragao do set, ou vazio se nao ha mundo aqui pra ancorar um.</returns>
	internal string EsferasNoTrailer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return "";

		SetDeEsferas? s = _sets.Find(x => x.Zona.Hash == pl.Zone.Hash);
		if (s == null)
		{
			int novo = ProximoIdDeSet();
			if (novo < 0 || CorpoDaZona(pl.Zone) == null) return "";

			s = new SetDeEsferas
			{
				Id = novo,
				Ex = pl.Pos.X,
				Ey = pl.Pos.Y - 4 * ZoneCollision.TileSize,
				CriadorSig = pl.Assinatura,
				CriadorConta = pl.Conta,
				CriadorNome = pl.Name,
				Poder = pl.Ficha.BP,
				Desejos = 1,
				OffTime = Esferas.OffTimeDeJogador,
			};
			s.PorZona(pl.Zone);
			_sets.Add(s);
			RefazerAsEsferas(s);
		}

		s.Inerte = false;
		s.AtivoEm = 0;
		s.Pedidos = 0;

		// A RODA: as sete dentro do alcance da invocacao (`Esferas.AlcanceDaInvocacao`, medido por eixo).
		int k = 0;
		foreach (Esfera e in _esferas.Where(x => x.Set == s.Id).OrderBy(x => x.Numero))
		{
			double ang = k++ * (Math.PI * 2 / Esferas.Total) - Math.PI / 2;
			e.Portador = 0;
			e.PorZona(pl.Zone);
			e.X = pl.Pos.X + (float)(Math.Cos(ang) * 0.9 * Esferas.AlcanceDaInvocacao);
			e.Y = pl.Pos.Y + (float)(Math.Sin(ang) * 0.6 * Esferas.AlcanceDaInvocacao);
			e.PrecisaAssentar = false;
		}

		MandarEsferas(pl.Zone);
		return s.NomeDoDragao;
	}

	/// <summary>
	/// ONDE HA GENTE NESTA ZONA: o habitante com mais vizinhos em volta (a doze tiles), e quantos sao.
	/// O povoamento espalha os cidadaos pelo habitat do planeta (`Core/Ai/Habitat`), e a cena da cidade
	/// precisa comecar onde eles estao -- e nao onde o diretor acha que uma cidade deveria estar.
	/// </summary>
	internal (bool Achou, Vec2 Onde, int Quantos) OndeHaGenteNoTrailer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (false, default, 0);

		List<ServerPlayer> povo = [.. ZoneList(pl.Zone.Hash).Where(o => o.Peer == null && o.Papel != null && !o.Ficha.dead)];
		const float Raio = 12 * ZoneCollision.TileSize;
		ServerPlayer? centro = null;
		int melhor = 0;
		foreach (ServerPlayer a in povo)
		{
			int perto = povo.Count(b => Vec2.Distance(a.Pos, b.Pos) <= Raio);
			if (perto > melhor) { melhor = perto; centro = a; }
		}
		return centro == null ? (false, default, 0) : (true, centro.Pos, melhor);
	}

	/// <summary>TIRA DO MUNDO os NPCs que a cena chamou -- toda cena que chama NPC acaba por aqui.</summary>
	internal void LimparOTrailer()
	{
		foreach (int id in _nascidosDoTrailer)
			if (_players.TryGetValue(id, out ServerPlayer? npc)) RemoverNpc(npc);
		_nascidosDoTrailer.Clear();
	}
}
