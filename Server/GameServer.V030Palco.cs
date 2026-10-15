using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagv030` (a versao 0.3.0 na tela): o robo e um cliente no mesmo processo do servidor
/// (`--host`), e estas portas montam a cena do lado de la -- a raiva, o rabo perdido, a arte dos pesos, um aviso num
/// tom. Cada uma escreve o MINIMO que a cena pede; a regra que se mede continua sendo a de producao (o retrato do poder,
/// o boneco do corpo, o funil do chat, a mochila).
/// </summary>
public sealed partial class GameServer
{
	/// <summary>A raiva deste corpo, escrita nas duas janelas (60 s), pra o HUD desenha-la. `Nenhuma` apaga as duas.</summary>
	internal void PalcoV30_Raiva(int id, NivelDeRaiva n)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		long ate = NowMs() + 60_000;
		pl.RaivaComumAte = n == NivelDeRaiva.Comum ? ate : 0;
		pl.FuriaExtremaAte = n == NivelDeRaiva.Extrema ? ate : 0;
	}

	/// <summary>O rabo deste corpo arrancado PRA SEMPRE, com a ferida aberta por estes segundos. Falso sem rabo.</summary>
	internal bool PalcoV30_RaboPerdido(int id, double segundosDeFerida)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate?.Corpo.Achar(Jandirus.Core.Races.RaboDoSaiyajin.Membro) is not { } rabo)
			return false;
		rabo.Decepado = true;
		rabo.Perdido = true;
		rabo.FeridaAberta = segundosDeFerida;
		pl.Combate.SincronizarVida();
		return true;
	}

	/// <summary>Passa o relogio das feridas que fecham pela porta de producao (`TickDaFeridaDoRabo`).</summary>
	internal void PalcoV30_PassarOTempoDaFerida(double segundos) => TickDaFeridaDoRabo(segundos);

	/// <summary>O rabo de volta, inteiro -- a bancada nao deixa o personagem dela sem rabo.</summary>
	internal void PalcoV30_DevolverORabo(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate?.Corpo.Achar(Jandirus.Core.Races.RaboDoSaiyajin.Membro) is not { } rabo)
			return;
		rabo.Decepado = false;
		rabo.Perdido = false;
		rabo.FeridaAberta = 0;
		rabo.Vida = rabo.VidaMax;
		pl.Combate.SincronizarVida();
	}

	/// <summary>
	/// Escolhe pros pesos deste corpo uma arte que nao e a de fabrica, pela porta de producao (`EscolherAArteDosPesos`), e
	/// devolve o nome dela e o caminho da folha. Vazios sem catalogo.
	/// </summary>
	internal (string Nome, string Caminho) PalcoV30_ArteDosPesos(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _visual == null) return ("", "");
		(string Nome, string Caminho) outra = PesoDeTreino.Escolhiveis(_visual).Find(e => e.Nome != PesoDeTreino.ArtePadrao);
		if (string.IsNullOrEmpty(outra.Nome)) return ("", "");
		EscolherAArteDosPesos(pl, outra.Nome);
		return outra;
	}

	/// <summary>Os pesos voltam a arte de fabrica.</summary>
	internal void PalcoV30_SemArteDosPesos(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.pesoArte = "";
		pl.SigAtributos = "";
	}

	/// <summary>(2026-10-15) O corpo se desfaz em pedacos pela porta do Core (`CombatState.Desfazer`) -- o que se mede e a tela.</summary>
	internal bool PalcoV30_Desfazer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate is not { } c) return false;
		c.Desfazer();
		return true;
	}

	/// <summary>O corpo em pedacos se refaz AGORA -- o fim do relogio sem esperar os 30 s (`Levantar`).</summary>
	internal void PalcoV30_Refazer(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate is { Desfeito: true } c) c.Levantar();
	}

	/// <summary>
	/// (2026-10-15) Alterna a Forma Gigante deste corpo pela tecnica de producao (`FormaGigante`), sem a trava de quem
	/// acabou de mudar de tamanho. Devolve se ele ficou gigante.
	/// </summary>
	internal bool PalcoV30_Gigante(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		_giganteTravaG2.Remove(id);
		FormaGigante(pl);
		return TemBuff(pl, BuffGiganteG2);
	}

	/// <summary>(2026-10-15) Um golpe de tecnica deste corpo naquele, pelo funil de producao das tecnicas que socam sozinhas (`GolpeG3`).</summary>
	internal bool PalcoV30_GolpeDeTecnica(int idDeQuemBate, int idDoAlvo)
	{
		if (!_players.TryGetValue(idDeQuemBate, out ServerPlayer? a) || !_players.TryGetValue(idDoAlvo, out ServerPlayer? d)) return false;
		GolpeG3(a, d, addDano: 0, nivel: 2);
		return true;
	}

	/// <summary>Um aviso do servidor neste tom, pelo funil de producao (`Avisar`).</summary>
	internal void PalcoV30_Avisar(int id, string texto, Protocol.Fala tom)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) Avisar(pl, texto, tom);
	}

	// =====================================================================
	// (v0.3.0, itens 7 e 11) A MESA DO ENMA: o passe e a reencarnacao pelos verbos de producao que o robo manda
	// =====================================================================
	/// <summary>Este corpo morto, de pe e de aureola, um tile a leste do Enma, com este zeni e o karma neutro. Falso sem o Enma.</summary>
	internal bool PalcoV30_MortoAoLadoDoEnma(int id, double zeni)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		if (_noChao.Find(o => o.Tipo == Alem.TipoDoEnma && o.Zona.Equals(alem)) is not { } enma) return false;
		pl.Ficha.dead = true;
		pl.Ficha.KO = false;
		// A BRIGA ACABA COM A MORTE: o `Morrer()` de producao zera as marcas de luta (`CombatState.Morrer`), e este palco escreve a
		// morte direto -- sem estas linhas o fantasma ainda "brigava" e a meditacao era recusada (achado da familia P da `--diagv030`).
		pl.Combate.EmCombate = 0;
		pl.Combate.LutandoDeVerdade = 0;
		pl.MorteJaViajou = true;
		pl.RelogioDaMorte = long.MaxValue;
		pl.Karma = Math.Max(pl.Karma, 0);
		pl.Ficha.hell_lockout_until = 0;
		pl.Ficha.passe_do_enma_ate = 0;
		pl.Ficha.Zeni = zeni;
		MoveToZone(pl.Id, alem, new Vec2(enma.X + ZoneCollision.TileSize, enma.Y));
		MandarFicha(pl);
		return true;
	}

	/// <summary>De volta a vida pelo `Renascer` de producao (o berco) -- a bancada nao deixa o personagem dela morto.</summary>
	internal void PalcoV30_Reviver(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !pl.Ficha.dead) return;
		pl.Ficha.passe_do_enma_ate = 0;
		Renascer(pl);
		MandarFicha(pl);
	}

	/// <summary>O BP base deste corpo -- o robo arma a reencarnacao com um numero conhecido.</summary>
	internal void PalcoV30_BpBase(int id, double bp)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Ficha.BP = bp;
	}

	/// <summary>(v0.3.0, item 30) O Spin Attack deste corpo pelo funil de producao (`GiratorioG7`), com o Ki cheio e a recarga zerada.</summary>
	internal void PalcoV30_Giro(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		_prontoG3.Remove(id);
		GiratorioG7(pl);
	}

	/// <summary>(v0.3.0, item 33) A marca deste corpo (0 = ninguem): a ronda das miragens depende dela.</summary>
	internal void PalcoV30_Marcar(int id, int alvo)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.AlvoId = alvo;
	}

	/// <summary>(v0.3.0, item 33) As miragens da Zanzoken Afterimage deste corpo acabam agora (e a deflexao emprestada volta).</summary>
	internal void PalcoV30_DesligarAsMiragens(int id) => EsquecerG14(id);

	/// <summary>(v0.3.0, item 28) A porcentagem guardada dos pesos deste corpo some -- o personagem que nunca ajustou.</summary>
	internal void PalcoV30_EsquecerAPorcentagem(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.pesoPct = 0;
		pl.SigAtributos = "";
	}

	/// <summary>A conta e o nome deste corpo.</summary>
	internal (string Conta, string Nome) PalcoV30_QuemE(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.Conta, pl.Name) : ("", "");

	/// <summary>O corpo VIVO desta conta com este nome, e o BP base dele -- nulo se nao ha.</summary>
	internal (int Id, double BP)? PalcoV30_DaConta(string conta, string nome) =>
		_players.Values.FirstOrDefault(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase)
											&& string.Equals(p.Name, nome, StringComparison.OrdinalIgnoreCase)
											&& !p.PersonagemConsumido) is { } achado
			? (achado.Id, achado.Ficha.BP)
			: null;

	/// <summary>A conta (a viva, ou a do disco): este slot esta ocupado? E o eco que ela guarda (o BP com que o proximo nasce).</summary>
	internal (bool Ocupado, double? Eco) PalcoV30_Conta(string conta, int slot)
	{
		AccountSave? acc = ContaViva(conta) ?? _store?.Carregar(conta);
		return (acc?.Slots[slot] != null, acc?.Reencarnacao?.BPDoNovo());
	}

	/// <summary>
	/// Esquece o eco desta conta, nas copias vivas e no disco: a prova do defeito da despedida reencarna de verdade, e um eco
	/// pendurado faria o personagem da proxima rodada nascer forte.
	/// </summary>
	internal void PalcoV30_EsquecerOEco(string conta)
	{
		EmTodaCopiaViva(conta, v => v.Reencarnacao = null);
		if (_store?.Carregar(conta) is { Reencarnacao: not null } noDisco)
		{
			noDisco.Reencarnacao = null;
			GravarAConta(noDisco);
		}
	}

	// =====================================================================
	// (v0.3.0, item 5) O CHEFE DE SAGA CACA O PLANETA INTEIRO
	// =====================================================================
	/// <summary>
	/// Um Freeza a ~30 tiles deste corpo (longe dos vinte do `MAX_AGGRO_RANGE`) e a pergunta de producao (`PresaDoHostil`),
	/// tres vezes: fora de uma saga, da saga, e da saga com o defeito do raio comum. O chefe sai do mundo na mesma chamada --
	/// nenhum tique de IA roda no meio. A distancia vai junto (o palco procura chao nos quatro rumos).
	/// </summary>
	internal (bool DaSaga, bool Avulso, bool ComDefeito, float Tiles) PalcoV30_ChefeCacaOPlaneta(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return default;
		const float t = ZoneCollision.TileSize;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		Vec2 longe = pl.Pos;
		foreach (Vec2 rumo in new[] { new Vec2(1, 0), new Vec2(-1, 0), new Vec2(0, 1), new Vec2(0, -1) })
		{
			Vec2 alvo = pl.Pos + rumo * (30 * t);
			Vec2 achado = mapa?.PontoLivrePerto(alvo) ?? alvo;
			if ((achado - pl.Pos).Length > (longe - pl.Pos).Length) longe = achado;
			if ((longe - pl.Pos).Length >= 25 * t) break;
		}
		ServerPlayer? chefe = NascerNpc("freeza_vegeta", pl.Zone, longe, 9_200);
		if (chefe?.Papel == null) return default;
		try
		{
			float tiles = (chefe.Pos - pl.Pos).Length / t;
			chefe.Papel.DaSaga = false;
			bool avulso = PresaDoHostil(chefe) == pl;
			chefe.PresaEngajada = 0;
			chefe.Papel.DaSaga = true;
			bool daSaga = PresaDoHostil(chefe) == pl;
			chefe.PresaEngajada = 0;
			ChefeComRaioComumDeTeste = true;
			bool comDefeito = PresaDoHostil(chefe) == pl;
			return (daSaga, avulso, comDefeito, tiles);
		}
		finally
		{
			ChefeComRaioComumDeTeste = false;
			RemoverNpc(chefe);
		}
	}

	// =====================================================================
	// (v0.3.0, item 35) O CHEFE DE SAGA, pelo nascimento de producao
	// =====================================================================
	private readonly List<ServerPlayer> _chefesDaBancadaV30 = [];

	/// <summary>Um chefe de saga NASCE pela porta de producao (`NascerChefeDaSaga`, a mesma da cadeia), no planeta pedido.</summary>
	private ServerPlayer? NascerChefeDeBancadaV30(string molde, string planeta)
	{
		if (_moldes?.Get(molde) is not { } m) return null;
		var ec = new EstadoDoChefe { Molde = molde, Bps = [.. m.Estagios.Select(s => s.Bp)] };
		var est = new EstadoDoElo { Id = "bancada_dv30_" + molde, Chefes = [ec] };
		var elo = new EloDaSaga
		{
			Id = est.Id, Nome = "bancada", Planeta = planeta,
			Chefes = [new ChefeDaSaga { Molde = molde, AnuncioDaChegada = $"{m.Nome} chegou -- e a bancada viu." }],
		};
		NascerChefeDaSaga(elo, est, ec);
		if (!_players.TryGetValue(ec.Corpo, out ServerPlayer? c)) return null;
		_chefesDaBancadaV30.Add(c);
		return c;
	}

	/// <summary>A chegada pra todos: um Freeza nasce NOUTRO planeta (o host nao o ve no mundo -- so a cena). O id, o nome e o planeta.</summary>
	internal (int Id, string Nome, string Planeta) PalcoV30_ChegadaDoChefe(int id, bool semCena = false)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return default;
		string planeta = pl.Zone.Name == "Namek" ? "Vegeta" : "Namek";
		ChegadaSemCenaDeTeste = semCena;
		try { return NascerChefeDeBancadaV30("freeza_namek", planeta) is { } c ? (c.Id, c.Name, NomeDoPlaneta(planeta)) : default; }
		finally { ChegadaSemCenaDeTeste = false; }
	}

	/// <summary>Um Freeza de saga AQUI, a tres tiles do host e sem mente -- a pergunta e a cena da troca de corpo, e nao a luta.</summary>
	internal int PalcoV30_ChefeAqui(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !pl.Zone.Equals(ZoneKey.Premade(pl.Zone.Name))) return 0;
		if (NascerChefeDeBancadaV30("freeza_namek", pl.Zone.Name) is not { } c) return 0;
		c.Cerebro = null;
		LargarOInput(c);
		Vec2 perto = MapaDaZonaOuCatalogo(pl.Zone)?.PontoLivrePerto(pl.Pos + new Vec2(3 * ZoneCollision.TileSize, 0)) ?? pl.Pos;
		CravarPosicao(c, perto);
		return c.Id;
	}

	/// <summary>O degrau seguinte do chefe pelo `AvancarEstagio` de producao -- o mesmo que o dano dispara. Devolve se ele ficou intocavel (em cena).</summary>
	internal bool PalcoV30_AvancarOChefe(int chefe, bool semCena = false)
	{
		if (!_players.TryGetValue(chefe, out ServerPlayer? c) || c.Papel == null) return false;
		ChefeSemCenaDeTeste = semCena;
		try { AvancarEstagio(c); }
		finally { ChefeSemCenaDeTeste = false; }
		return c.Combate.Intocavel;
	}

	/// <summary>Tira do mundo os chefes que a bancada fez nascer.</summary>
	internal void PalcoV30_TirarOsChefes()
	{
		foreach (ServerPlayer c in _chefesDaBancadaV30) if (_players.ContainsKey(c.Id)) RemoverNpc(c);
		_chefesDaBancadaV30.Clear();
	}

	// =====================================================================
	// (v0.3.0, item 22) O ULTIMATO DO FREEZA, pelo caminho de producao da saga
	// =====================================================================
	private (EloDaSaga Elo, EstadoDoElo Est, EstadoDoChefe Ec, ServerPlayer Chefe, int Quem, ZoneKey Volta, Vec2 VoltaPos, double VidaAntes)? _ultimatoV30;

	/// <summary>
	/// Este corpo vai (vivo) pro Outro Mundo, um Freeza de saga nasce a cinco tiles dele, e o ULTIMATO da saga de Vegeta
	/// ja vencido roda uma vez -- e ele ergue a Supernova (`CorrerOUltimato`, a porta de producao). A saga e uma COPIA com
	/// o planeta trocado pelo Outro Mundo, que NAO e um planeta: na explosao o `ComecarDestruicao` recusa, e nada e
	/// destruido. Devolve o id do chefe (0 = nao montou).
	/// </summary>
	internal int PalcoV30_Ultimato(int id)
	{
		PalcoV30_DesfazerOUltimato();
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _moldes?.Cadeia.FirstOrDefault(e => e.Id == "freeza_vegeta") is not { } vegeta)
			return 0;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		ZoneKey volta = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		if (!pl.Zone.Equals(alem)) MoveToZone(pl.Id, alem, MesaDoEnma(alem));
		Vec2 onde = MapaDaZonaOuCatalogo(alem)?.PontoLivrePerto(pl.Pos + new Vec2(5 * ZoneCollision.TileSize, 0))
					?? pl.Pos + new Vec2(5 * ZoneCollision.TileSize, 0);
		ServerPlayer? chefe = NascerNpc("freeza_vegeta", alem, onde, 9_300);
		if (chefe?.Papel == null) return 0;
		chefe.Papel.DaSaga = true;

		var elo = new EloDaSaga
		{
			Id = "bancada_supernova", Nome = vegeta.Nome, Planeta = alem.Name, DestroiPlaneta = true,
			MinutosDeLuta = vegeta.MinutosDeLuta, MinutosDeOcio = vegeta.MinutosDeOcio,
			AnuncioDoUltimato = vegeta.AnuncioDoUltimato, Chefes = vegeta.Chefes,
		};
		var ec = new EstadoDoChefe
		{
			Molde = "freeza_vegeta", Fase = FaseDoChefe.NoMundo, Corpo = chefe.Id, Engajou = true,
			UltimatoEm = _relogioDoMundo - 1,
		};
		var est = new EstadoDoElo { Id = elo.Id, Fase = FaseDaSaga.EmCurso, Chefes = [ec] };
		_ultimatoV30 = (elo, est, ec, chefe, pl.Id, volta, voltaPos, pl.Ficha.KO ? -1 : pl.Ficha.HP);
		CorrerOUltimato(elo, est, ec, chefe);
		return chefe.Id;
	}

	/// <summary>Uma volta do ultimato, como o tique da saga faria. Verdadeiro enquanto o chefe ainda esta no mundo.</summary>
	internal bool PalcoV30_TiqueDoUltimato()
	{
		if (_ultimatoV30 is not { } u || !_players.ContainsKey(u.Chefe.Id)) return false;
		CorrerOUltimato(u.Elo, u.Est, u.Ec, u.Chefe);
		return _players.ContainsKey(u.Chefe.Id);
	}

	/// <summary>
	/// Quem a bancada pos ao lado do chefe saiu do ultimato INTEIRO? De pe e com a vida de quando a Supernova comecou: o
	/// chefe com o sol erguido a cinco tiles nao soca, nao investe e nao atira (`MaosOcupadasDaIa`).
	/// </summary>
	internal (bool Intacto, double Antes, double Agora) PalcoV30_IntactoNoUltimato() =>
		_ultimatoV30 is { } u && _players.TryGetValue(u.Quem, out ServerPlayer? pl)
			? (u.VidaAntes >= 0 && !pl.Ficha.KO && pl.Ficha.HP >= u.VidaAntes - 0.5, u.VidaAntes, pl.Ficha.HP)
			: default;

	/// <summary>O chefe esta concentrando a Supernova? Intocavel? Parado?</summary>
	internal (bool Supernova, bool Intocavel, bool Parado) PalcoV30_ComoEstaOChefe(int id) =>
		_players.TryGetValue(id, out ServerPlayer? c)
			? (OcupacaoDe(c) == Ocupacao.Supernova, c.Combate?.Intocavel == true, !PodeMexerOCorpo(c))
			: default;

	// =====================================================================
	// (v0.3.0, adendo 11) O REFUGIO PELA TERRA: o natal deste corpo morre num palco, e a oferta sai pelo funil de producao
	// =====================================================================
	private PalcoDeMortes? _refugioV30Palco;
	private Jandirus.Core.Races.Berco _refugioV30Berco;

	/// <summary>
	/// O NATAL DESTE CORPO ACABA: o berco passa a ser Namek, Namek morre num palco de bancada (o `planetas-mortos.json` do
	/// dono fica intacto) e a oferta do refugio sai como na chegada ao Outro Mundo -- a tela abre sozinha. Falso se Namek nao
	/// morreu.
	/// </summary>
	internal bool PalcoV30_SemNatal(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		PalcoV30_DesfazerSemNatal(id);
		_refugioV30Berco = pl.Berco;
		pl.Berco = new Jandirus.Core.Races.Berco { Planeta = "Namek", PreFeito = true, Natal = "Namek" };
		_refugioV30Palco = PalcoDeMortesDeBancada();
		if (!MatarPlanetaNoPalco(ZoneKey.Premade("Namek"), "diagv030: o refugio pela Terra")) return false;
		pl.RefugioJaOferecido = false;
		OferecerORefugio(pl, podeAbrir: true);
		return true;
	}

	/// <summary>A oferta de novo, sem pedir a tela -- o robo a usa depois de ligar o defeito.</summary>
	internal void PalcoV30_OfertaDoRefugio(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) OferecerORefugio(pl, podeAbrir: false);
	}

	/// <summary>O que o servidor guarda: este corpo escolheu voltar pela Terra?</summary>
	internal bool PalcoV30_RefugioNaTerra(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && pl.RefugioNaTerra;

	/// <summary>Fecha o palco: Namek volta, o berco de antes volta, a escolha se apaga e a tela recebe o "berco de pe".</summary>
	internal void PalcoV30_DesfazerSemNatal(int id)
	{
		if (_refugioV30Palco is not { } palco) return;
		_refugioV30Palco = null;
		palco.Dispose();
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Berco = _refugioV30Berco;
		pl.RefugioNaTerra = false;
		ComandoDeRefugio(pl, "refugio", "");   // com o berco de pe ele manda o pacote "limpo" -- a tela e o cartao somem
	}

	/// <summary>Fecha o palco do ultimato: o chefe sai do mundo (se ainda esta) e o corpo volta pra onde estava.</summary>
	internal void PalcoV30_DesfazerOUltimato()
	{
		if (_ultimatoV30 is not { } u) return;
		_ultimatoV30 = null;
		if (_players.ContainsKey(u.Chefe.Id)) RemoverNpc(u.Chefe);
		if (_players.TryGetValue(u.Quem, out ServerPlayer? pl) && !pl.Zone.Equals(u.Volta)) MoveToZone(pl.Id, u.Volta, u.VoltaPos);
	}
}
