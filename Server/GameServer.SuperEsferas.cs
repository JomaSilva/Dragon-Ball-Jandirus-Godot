using System.Text.Json;
using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// UMA SUPER ESFERA -- uma linha do `pspace_superdb` do DM (ProceduralSpace.dm:54).
///
/// ============================ O QUE **NAO** ESTA AQUI E A POSICAO ============================
/// O DM guarda `list(num, sx, sy, x, y, held, owner_sig, owner_name)` -- oito campos, cinco deles de
/// lugar. Aqui ha tres: numero, quem e o nome de quem. A posicao sai de
/// <see cref="SuperEsferas.PosicaoDa"/>, funcao pura de (seed do universo, numero, ciclo), e o ciclo
/// e um inteiro unico pro set inteiro.
///
/// Isso resolve, com um numero, a tensao que a Fase 0 nomeou como o item 1 da especificacao: *"as
/// Super Esferas nao podem ser funcao pura da seed -- elas tem dono e mudam de lugar"*. Elas podem:
/// **o que tem dono e o CLAIM e o que muda de lugar e o CICLO**. Cliente e servidor chegam na mesma
/// posicao sem trocar mapa, e o unico dado autoritativo e a posse -- exatamente como um dominio de
/// conquista.
///
/// O CAMPO `held` DO DM **NAO FOI PORTADO**, e a Fase 0 mediu o porque: ele nunca e escrito como 1 em
/// lugar nenhum do codigo vivo (so zerado, em :1432 e :1450). Ele so podia valer 1 vindo de um
/// savefile ANTIGO, de quando a esfera era carregavel no inventario -- ou seja, e tolerancia de
/// leitura de save velho de OUTRO jogo, e nao mecanica. Portar seria portar um campo morto.
/// ========================================================================================
/// </summary>
public sealed class SuperEsfera
{
	/// <summary>1..7.</summary>
	public int Numero;

	/// <summary>
	/// A ASSINATURA de quem a reivindicou. Vazio = livre.
	///
	/// Por assinatura (personagem) e nao por conta, como o <see cref="Dominio.Assinatura"/> -- e a
	/// mesma pergunta que o DM faz (`E[7] = usr.signature`, :1539).
	/// </summary>
	public string Dono = "";

	/// <summary>O nome que a tela le. O livro guarda o ultimo conhecido, como o dominio.</summary>
	public string DonoNome = "";
}

/// <summary>Um canal de reivindicacao em pe -- `sdb_claim_channel` / `sdb_contest_channel`.</summary>
public sealed class DisputaDeSuper
{
	public int Numero;
	public int Quem;

	/// <summary>Segundos que faltam. Conta pra baixo no tique de SEGUNDO -- ver `TickDasSuperEsferas`.</summary>
	public double Faltam;

	/// <summary>Isto e uma tomada (havia dono) ou uma reivindicacao de esfera livre?</summary>
	public bool Tomada;

	/// <summary>Quem era o dono quando o canal abriu -- pra o recado e pra a guarda de corrida.</summary>
	public string DonoNoInicio = "";

	/// <summary>Pra o aviso de progresso sair de minuto em minuto, e nao trinta vezes por segundo.</summary>
	public double ProximoAviso;
}

/// <summary>
/// **AS SUPER ESFERAS DO DRAGAO EM JOGO** -- o registro, o radar dourado, o claim de 10 s e a disputa
/// de 5 min. Porte de `Code/Modules/Tech/ProceduralSpace.dm:1409-1728`.
///
/// ============================ ELE REUSA A CONQUISTA, E O PROPRIO DM MANDA REUSAR ============================
/// A instrucao da tarefa foi *"REUSE o que o port ja tem (a fase 0 apontou primos: conquista de
/// planeta, reputacao). Nao invente um segundo eixo para a mesma ideia"* -- e o original concorda com
/// ela por escrito: o `sdb_contest_channel` chama **`conq_notify_owner()`** (:1547), que e literalmente
/// a funcao de aviso da conquista de planetas.
///
/// Entao o recado ao dono aqui e o <see cref="RecadoAoDono"/> do `GameServer.Conquista.cs`, sem uma
/// linha nova: quem estiver online le na hora, quem estiver fora le no proximo login, e a fila vai pro
/// mesmo `conquista.json`. Uma segunda fila de recados seria um segundo eixo pra mesma ideia -- e a
/// primeira a divergir seria a que ninguem lembrasse de entregar no login.
/// =========================================================================================================
///
/// ============================ O DESEJO **NAO** ESTA AQUI: E A FASE 2 ============================
/// `Invocar_Super_Shenron` (:1578), a LINGUA DOS DEUSES (`godtongue_check`, WishTable.dm:37) e o
/// PROCURADOR (`Transferir_Super_Esferas`, :1651) ficam pra Fase 2. O ponto de plugue e
/// <see cref="ChamarOSuperShenron"/>, e o contrato esta escrito la.
/// ==========================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>As sete. Lista fixa -- nasce cheia e nunca muda de tamanho.</summary>
	private readonly List<SuperEsfera> _supers = [];

	/// <summary>
	/// **QUANTAS VEZES AS SETE JA FORAM CONSUMIDAS.** E o unico estado de posicao que existe.
	///
	/// `pspace_sdb_scatter()` (:1642) re-espalha depois de cada desejo; aqui re-espalhar e `+1`.
	/// </summary>
	private int _cicloDasSupers;

	/// <summary>Os canais em pe, por jogador. Um jogador so disputa uma esfera por vez.</summary>
	private readonly Dictionary<int, DisputaDeSuper> _disputasDeSuper = [];

	/// <summary>O que as Super disseram -- ligada so pela bancada.</summary>
	internal static List<string>? EscutaDasSupers;

	private string CaminhoDasSupers =>
		System.IO.Path.Combine(_store?.Pasta ?? ".", "superesferas.json");

	// =====================================================================
	// DISCO
	// =====================================================================
	private sealed class LivroDasSupers
	{
		/// <summary>PRIMEIRO CAMPO de proposito, como o carimbo do `LivroDeConquista`: um save antigo
		/// sem ele continua sendo JSON valido, e o ciclo cai em zero -- que e o universo recem-nascido.</summary>
		public int Ciclo;

		public List<SuperEsfera> Esferas = [];

		// ============================ A PROCURACAO VAI PRO DISCO, COMO NO DM ============================
		// O original persiste `sdb_benef_sig`/`sdb_benef_name` no savefile "Galaxy" (`:186-187`), e tem
		// que persistir: emprestar as sete e um ato do jogo, e um reinicio de servidor nao pode
		// transformar "emprestei" em "dei". O PEDIDO REGISTRADO vai junto pelo mesmo motivo -- ele e a
		// unica coisa que impede o falante de escolher.
		//
		// O que **nao** vai: as ofertas em aberto e a confirmacao do preco. Sao dialogos de sessao, com
		// prazo de um minuto -- e voltar do disco com uma pergunta de vida ou morte pendente seria pior
		// que perde-la.
		// ==========================================================================================
		public string BenefSig = "", BenefNome = "";
		public string Pedido = "", AlvoDoPedido = "";
	}

	private void CarregarSupers()
	{
		try
		{
			if (System.IO.File.Exists(CaminhoDasSupers))
			{
				LivroDasSupers? l = JsonSerializer.Deserialize<LivroDasSupers>(
					System.IO.File.ReadAllText(CaminhoDasSupers),
					new JsonSerializerOptions { IncludeFields = true });

				if (l != null)
				{
					_cicloDasSupers = l.Ciclo;
					_supers.AddRange(l.Esferas);
					_benefSig = l.BenefSig;
					_benefNome = l.BenefNome;
					_pedidoDoBenef = l.Pedido;
					_alvoDoPedido = l.AlvoDoPedido;
				}
			}
		}
		catch (Exception e) { GD.PushWarning($"[server] superesferas.json ilegivel: {e.Message}"); }

		// SANEAMENTO: sete, numeradas de 1 a 7, sem buraco e sem repetida. O registro do DM nasce
		// assim (`for(n=1 to 7)`, :1426) e nunca muda de tamanho; um save mutilado nao pode deixar o
		// jogo com seis -- ninguem completaria as sete e o sistema inteiro ficaria mudo, sem erro.
		for (int n = 1; n <= SuperEsferas.Total; n++)
			if (!_supers.Any(s => s.Numero == n)) _supers.Add(new SuperEsfera { Numero = n });
		_supers.RemoveAll(s => s.Numero < 1 || s.Numero > SuperEsferas.Total);

		// ============================ UM CARGO DIVINO ORFAO GRITA NO BOOT ============================
		// `LinguaDosDeuses.GlobaisDivinos` e a lista LITERAL do `divine_rank_now` (WishTable.dm:30-33), e
		// a traducao pra chave deste port e uma consulta a tabela de cargos. Um global que nao ache
		// cargo **nao quebra nada**: o cargo continua existindo, o jogador continua sendo Kaio do
		// Norte, e a lingua simplesmente nunca chega nele. Silencio perfeito.
		//
		// Por isso ele sai aqui, junto do resto do estado das Super Esferas -- e nao so na bancada. Ver o
		// mesmo argumento em `Esferas.OqueFalta`: divida que nao se mostra e divida que ninguem paga.
		// ========================================================================================
		string[] semCargo = [.. LinguaDosDeuses.GlobaisSemCargo];
		if (semCargo.Length > 0)
			GD.PushWarning($"[server] LINGUA DOS DEUSES: {semCargo.Length} cargo(s) divino(s) do DM nao "
						 + $"acham cargo neste port -- {string.Join(", ", semCargo)}. Quem os carregar "
						 + "NAO vai aprender a lingua, e nada mais vai avisar.");

		int livres = _supers.Count(s => s.Dono.Length == 0);
		GD.Print($"[server] super esferas: ciclo {_cicloDasSupers}, {livres}/{SuperEsferas.Total} livres"
			   + $" | lingua dos deuses: {LinguaDosDeuses.ChavesDivinas.Count()} cargos + sangue "
			   + string.Join("/", LinguaDosDeuses.RacasDivinas));
		foreach (SuperEsfera s in _supers.OrderBy(s => s.Numero))
		{
			(int sx, int sy) = SuperEsferas.CelulaDa(SeedDoUniverso, s.Numero, _cicloDasSupers);
			GD.Print($"          #{s.Numero} em [{sx}:{sy}]"
				   + (s.Dono.Length > 0 ? $" -- de {s.DonoNome}" : ""));
		}

		SalvarSupers();
	}

	private void SalvarSupers()
	{
		if (_store == null) return;
		try
		{
			var l = new LivroDasSupers
			{
				Ciclo = _cicloDasSupers,
				Esferas = _supers,
				BenefSig = _benefSig,
				BenefNome = _benefNome,
				Pedido = _pedidoDoBenef,
				AlvoDoPedido = _alvoDoPedido,
			};
			string tmp = CaminhoDasSupers + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(l,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDasSupers, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar superesferas.json: {e.Message}"); }
	}

	// =====================================================================
	// CONSULTA
	// =====================================================================
	/// <summary>Onde a esfera `n` esta AGORA. Funcao pura -- ver <see cref="SuperEsferas.PosicaoDa"/>.</summary>
	public Vec2 OndeEstaASuper(int numero) =>
		SuperEsferas.PosicaoDa(SeedDoUniverso, numero, _cicloDasSupers);

	/// <summary>`sdb_owned_count` (:1456) -- quantas esta assinatura tem. E o gate das sete.</summary>
	public int QuantasSupersTem(string assinatura) => assinatura.Length == 0
		? 0
		: _supers.Count(s => string.Equals(s.Dono, assinatura, StringComparison.Ordinal));

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// UMA VEZ POR SEGUNDO. Os canais andam e os que perderam as condicoes caem.
	///
	/// ============================ AS CINCO CONDICOES DE ABORTO SAO A DEFESA INTEIRA ============================
	/// O DM as repete nos dois canais (:1531-1535 e :1556-1559): o disputante sumiu (logout), morreu,
	/// nocauteou, a esfera perdeu o lugar, ou ele se afastou de <see cref="SuperEsferas.AlcanceDaDisputa"/>.
	///
	/// Elas sao a regra de defesa do jogo inteiro: **o dono nao precisa vencer o ladrao**. E por isso
	/// que elas nao viraram cinco `if` espalhados -- estao num lugar so, e o motivo da queda vai na
	/// mensagem, senao o defensor nunca saberia o que funcionou.
	/// =========================================================================================================
	/// </summary>
	private void TickDasSuperEsferas()
	{
		if (_disputasDeSuper.Count == 0) return;

		foreach ((int id, DisputaDeSuper d) in _disputasDeSuper.ToList())
		{
			ServerPlayer? quem = _players.GetValueOrDefault(id);
			SuperEsfera? alvo = _supers.Find(s => s.Numero == d.Numero);

			string queda = quem is not { Peer: not null } ? "o disputante saiu do mundo"
						 : quem.Ficha.dead ? "o disputante morreu"
						 : quem.Ficha.KO ? "o disputante caiu"
						 : alvo == null ? "a esfera saiu do registro"
						 : !Espaco.EhEspaco(quem.Zone) ? "o disputante deixou o espaço"
						 : (quem.Pos - OndeEstaASuper(d.Numero)).Length > SuperEsferas.AlcanceDaDisputa
							? "o disputante se afastou da esfera"
						 : "";

			// SEXTA CONDICAO, so na tomada (:1560-1562): o dono mudou no meio (admin, reset, outra
			// disputa). A disputa fica sem objeto e cai -- ela era contra AQUELE dono.
			if (queda.Length == 0 && d.Tomada && alvo != null
				&& !string.Equals(alvo.Dono, d.DonoNoInicio, StringComparison.Ordinal))
				queda = "a esfera trocou de dono no meio da disputa";

			if (queda.Length > 0)
			{
				_disputasDeSuper.Remove(id);
				if (quem != null) Avisar(quem, $"a disputa pela Super Esfera #{d.Numero} caiu: {queda}.");

				if (d.Tomada && d.DonoNoInicio.Length > 0)
					RecadoAoDono(d.DonoNoInicio,
						$"Sua Super Esfera #{d.Numero} está SEGURA -- a tentativa de tomá-la falhou ({queda}).");

				AnunciarSupers($"A disputa pela Super Esfera #{d.Numero} fracassou.");
				continue;
			}

			d.Faltam -= 1;

			if (d.Faltam > 0)
			{
				// O AVISO DE PROGRESSO. Cinco minutos de canal sem uma linha sao cinco minutos de tela
				// muda -- e e nesses cinco minutos que o dono tem que decidir se vem.
				if (d.Faltam <= d.ProximoAviso)
				{
					d.ProximoAviso -= SuperEsferas.SegundosEntreAvisos;
					Avisar(quem!, $"Super Esfera #{d.Numero}: faltam {d.Faltam:0} s.");
				}
				continue;
			}

			_disputasDeSuper.Remove(id);
			FecharADisputa(quem!, alvo!, d);
		}
	}

	/// <summary>O canal fechou: a esfera troca de mao. As duas vitorias passam por aqui.</summary>
	private void FecharADisputa(ServerPlayer quem, SuperEsfera alvo, DisputaDeSuper d)
	{
		// GUARDA CONTRA CORRIDA, literal do DM (:1538-1539): dois canais podem ter fechado no mesmo
		// segundo sobre a mesma esfera livre. Quem chegou primeiro fica.
		if (!d.Tomada && alvo.Dono.Length > 0)
		{
			Avisar(quem, $"tarde demais: a Super Esfera #{alvo.Numero} já é de {alvo.DonoNome}.");
			return;
		}

		string exDono = alvo.Dono, exNome = alvo.DonoNome;
		alvo.Dono = quem.Assinatura;
		alvo.DonoNome = quem.Name;
		SalvarSupers();

		// ============================ TOMAR A FORCA **QUEBRA A PROCURACAO** ============================
		// `:1571-1573` -- *"um claim tomado A FORCA quebra a transferencia pendente"*. E a quinta trava
		// contra o porta-voz infiel: sem ela, ele combinaria com um comparsa, deixaria o comparsa "tomar"
		// uma esfera, e a procuracao continuaria de pe apontando pro beneficiario errado.
		//
		// So na TOMADA (`d.Tomada`), e nao no claim de esfera livre: reivindicar o que nao e de ninguem
		// nao tem vitima, e derrubar a procuracao ali castigaria um doador que nem estava na historia.
		// =========================================================================================
		if (d.Tomada) QuebrarAProcuracao($"a esfera #{alvo.Numero} foi tomada à força por {quem.Name}");

		int quantas = QuantasSupersTem(quem.Assinatura);
		AnunciarSupers($"{quem.Name} reivindica a Super Esfera #{alvo.Numero}! "
					 + $"({quantas}/{SuperEsferas.Total})");

		if (exDono.Length > 0)
			RecadoAoDono(exDono, $"Você PERDEU a Super Esfera #{alvo.Numero} para {quem.Name}.");

		if (quantas >= SuperEsferas.Total)
			Avisar(quem, "as SETE são suas. Chame o Super Shenron -- se você souber falar com ele.");

		GD.Print($"[super] {quem.Name} tomou a #{alvo.Numero}"
			   + (exNome.Length > 0 ? $" de {exNome}" : " (livre)"));

		MandarSupers(quem);
	}

	// =====================================================================
	// REIVINDICAR -- `Click()` (:1505-1522)
	// =====================================================================
	private void ReivindicarSuper(ServerPlayer pl)
	{
		if (!Espaco.EhEspaco(pl.Zone))
		{ Avisar(pl, "as Super Esferas ficam no espaço -- não em terra firme."); return; }

		if (pl.Ficha.dead || pl.Ficha.KO) { Avisar(pl, "você não está em condições de reivindicar nada."); return; }

		if (_disputasDeSuper.ContainsKey(pl.Id))
		{ Avisar(pl, "você já está num canal de disputa."); return; }

		SuperEsfera? perto = null;
		double melhor = double.MaxValue;
		foreach (SuperEsfera s in _supers)
		{
			double d = (pl.Pos - OndeEstaASuper(s.Numero)).Length;
			if (d > SuperEsferas.AlcanceDeReivindicar || d >= melhor) continue;
			perto = s;
			melhor = d;
		}

		if (perto == null)
		{ Avisar(pl, "não há Super Esfera ao seu alcance. Chegue perto dela."); return; }

		if (string.Equals(perto.Dono, pl.Assinatura, StringComparison.Ordinal))
		{
			Avisar(pl, $"a Super Esfera #{perto.Numero} já é sua "
					 + $"({QuantasSupersTem(pl.Assinatura)}/{SuperEsferas.Total}).");
			return;
		}

		if (_disputasDeSuper.Values.Any(x => x.Numero == perto.Numero))
		{ Avisar(pl, $"a Super Esfera #{perto.Numero} já está sendo disputada agora."); return; }

		bool tomada = perto.Dono.Length > 0;
		double prazo = tomada ? SuperEsferas.SegundosDeDisputa : SuperEsferas.SegundosDeClaim;

		_disputasDeSuper[pl.Id] = new DisputaDeSuper
		{
			Numero = perto.Numero,
			Quem = pl.Id,
			Faltam = prazo,
			Tomada = tomada,
			DonoNoInicio = perto.Dono,
			ProximoAviso = prazo - SuperEsferas.SegundosEntreAvisos,
		};

		Avisar(pl, tomada
			? $"você começa a arrancar a Super Esfera #{perto.Numero} de {perto.DonoNome}. "
			  + $"{prazo:0} s -- e {perto.DonoNome} foi avisado."
			: $"você põe as mãos na Super Esfera #{perto.Numero}. {prazo:0} s parado, a até "
			  + $"{SuperEsferas.AlcanceDaDisputa / ZoneCollision.TileSize:0} tiles dela.");

		if (!tomada) return;

		AnunciarSupers($"{pl.Name} tenta TOMAR a Super Esfera #{perto.Numero} de {perto.DonoNome}!");

		// ============================ O RECADO E O DA CONQUISTA, LITERALMENTE ============================
		// `conq_notify_owner()` (:1547) -- a mesma funcao. Ele entrega na hora se o dono estiver
		// online, e ENFILEIRA pro proximo login dele se nao. E carrega a POSICAO, porque um aviso de
		// "venha defender" sem o lugar e um aviso que nao da pra atender.
		// ==========================================================================================
		Vec2 onde = OndeEstaASuper(perto.Numero);
		(int sx, int sy) = SuperEsferas.CelulaDa(SeedDoUniverso, perto.Numero, _cicloDasSupers);
		RecadoAoDono(perto.Dono,
			$"ALGUÉM está tomando sua Super Esfera #{perto.Numero}! Célula [{sx}:{sy}], "
			+ $"posição ({onde.X:0}, {onde.Y:0}). Você tem {prazo / 60:0} minuto(s).");
	}

	// =====================================================================
	// O PLUGUE DA FASE 2
	// =====================================================================
	// ============================ O PLUGUE DA FASE 2, JA LIGADO ============================
	// `ChamarOSuperShenron`, a LINGUA DOS DEUSES e o PROCURADOR moram em `GameServer.SuperShenron.cs`.
	// O contrato que a Fase 1 escreveu aqui foi cumprido sem reabrir este arquivo, e as quatro clausulas
	// dele valem por escrito: as sete antes da lingua, os DOIS eixos da lingua ligados, a transferencia
	// com beneficiario, e o consumo por `ConsumirAsSupers`.
	// ==================================================================================

	/// <summary>
	/// **AS SETE FORAM CONSUMIDAS.** Funil unico -- a Fase 2 chama isto e mais nada.
	///
	/// `pspace_sdb_scatter()` no fim do `Invocar_Super_Shenron` (:1642): os claims caem, a procuracao
	/// cai junto, e as sete reaparecem em lugares novos. Aqui "lugares novos" e `_cicloDasSupers + 1`,
	/// e a posicao continua sendo funcao pura da semente.
	/// </summary>
	private void ConsumirAsSupers()
	{
		_cicloDasSupers++;
		foreach (SuperEsfera s in _supers) { s.Dono = ""; s.DonoNome = ""; }
		_disputasDeSuper.Clear();

		// A PROCURACAO CAI JUNTO -- `pspace_sdb_scatter()` zera `sdb_benef_sig` porque as esferas que ela
		// descrevia deixaram de ser de alguem. Deixa-la de pe faria o proximo dono das sete invocar em
		// nome de um estranho que emprestou esferas OUTRAS, num ciclo anterior.
		_benefSig = "";
		_benefNome = "";
		_pedidoDoBenef = "";
		_alvoDoPedido = "";
		_precoPendente = default;
		_ofertasDeGuarda.Clear();

		SalvarSupers();

		AnunciarSupers("As sete Super Esferas se separam e voam para os confins da galáxia.");
		foreach (ServerPlayer p in Jogadores) MandarSupers(p);
	}

	// =====================================================================
	// REDE -- o radar dourado
	// =====================================================================
	/// <summary>
	/// AS SUPER ESFERAS QUE ESTE JOGADOR PODE VER, mais o sinal do radar e o placar dele.
	///
	/// ============================ POR QUE O SERVIDOR MANDA, SE A POSICAO E PURA ============================
	/// O cliente PODERIA calcular as sete sozinho -- a funcao esta no Core e ele tem a semente. Mandar
	/// mesmo assim e uma decisao, e ela e sobre SIGILO e nao sobre custo: o ciclo e o unico dado que
	/// falta pra montar o mapa do tesouro inteiro, e ele nao viaja. Sem ele, um cliente mexido nao tem
	/// como enumerar as sete.
	///
	/// E o recorte e o mesmo que o resto do espaco ja usa (a CHUNK, ver `Espaco.PertoDeMim`): a esfera
	/// so aparece quando se chega perto. E o `SaveItem = 0` do DM dito de outro jeito -- *"o obj so
	/// existe enquanto o setor esta carregado"*.
	/// ==================================================================================================
	/// </summary>
	private void MandarSupers(ServerPlayer pl)
	{
		if (pl.Peer == null) return;

		var perto = new List<(SuperEsfera S, Vec2 P)>();
		int maisProxima = int.MaxValue;
		Vec2 ondeAMaisProxima = default;
		int sentidas = 0;

		foreach (SuperEsfera s in _supers)
		{
			Vec2 p = OndeEstaASuper(s.Numero);
			if (Espaco.EhEspaco(pl.Zone) && Espaco.PertoDeMim(pl.Pos, p)) perto.Add((s, p));

			// O RADAR MEDE DA POSICAO DO CORPO, esteja ele onde estiver: o painel Nav do DM e do
			// piloto, e um radar que so funciona no vacuo seria inutil justamente pra quem pousou pra
			// procurar. A distancia e em CELULAS -- ver `SuperEsferas.CelulasEntre`.
			int celulas = SuperEsferas.CelulasEntre(pl.Pos, p);
			if (celulas > SuperEsferas.CelulasDeSinal) continue;

			sentidas++;
			if (celulas >= maisProxima) continue;
			maisProxima = celulas;
			ondeAMaisProxima = p;
		}

		var w = Protocol.Begin(Protocol.S2C.SuperEsferas);
		w.Put((byte)perto.Count);
		foreach ((SuperEsfera s, Vec2 p) in perto)
		{
			w.Put((byte)s.Numero);
			w.Put(p.X);
			w.Put(p.Y);
			w.Put(s.DonoNome);
			w.Put(string.Equals(s.Dono, pl.Assinatura, StringComparison.Ordinal));
		}
		w.Put((byte)QuantasSupersTem(pl.Assinatura));
		w.Put(sentidas == 0 ? "" : SuperEsferas.SinalDoRadar(maisProxima, ondeAMaisProxima, sentidas));

		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Uma linha pra todo mundo. Mesmo canal do `AnunciarNoMundo`; a escuta e da bancada.</summary>
	private void AnunciarSupers(string texto)
	{
		if (texto.Length == 0) return;
		EscutaDasSupers?.Add(texto);
		AnunciarNoMundo(texto);
	}

	// =====================================================================
	// OS VERBOS
	// =====================================================================
	private bool ComandoDeSuperEsferas(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "sdb_status": StatusDasSupers(pl); return true;
			case "sdb_reivindicar": ReivindicarSuper(pl); MandarSupers(pl); return true;
			case "sdb_invocar": ChamarOSuperShenron(pl, arg); return true;

			// ADMIN: `Galaxy_SuperDB_Reset()` (:1719-1728). Re-espalha e apaga os claims -- e o mesmo
			// funil do desejo, porque e a mesma coisa que acontece.
			case "sdb_reset":
				if (!EhAdmin(pl)) return false;
				ConsumirAsSupers();
				Avisar(pl, $"as sete Super Esferas foram re-espalhadas (ciclo {_cicloDasSupers}).");
				return true;

			// A LINGUA, O PROCURADOR E O DESEJO moram em `GameServer.SuperShenron.cs`. Uma entrada so no
			// roteador: dois `switch` do mesmo prefixo em arquivos diferentes seriam duas listas que
			// divergem no dia em que alguem renomeasse um verbo.
			default: return ComandoDoSuperShenron(pl, cmd, arg);
		}
	}

	/// <summary>
	/// `Super_Esferas_Status()` (:1683-1700) -- o placar SEM entregar onde elas estao.
	///
	/// Essa e a regra do verb no original e ela e boa: ele diz quem tem quantas e quantas estao
	/// livres, e nada de posicao. Quem quer o lugar usa o radar, e o radar exige chegar perto.
	/// </summary>
	private void StatusDasSupers(ServerPlayer pl)
	{
		Avisar(pl, "-- Super Esferas do Dragão --");
		Avisar(pl, $"  livres: {_supers.Count(s => s.Dono.Length == 0)}/{SuperEsferas.Total}");

		foreach (var g in _supers.Where(s => s.Dono.Length > 0)
								 .GroupBy(s => s.DonoNome)
								 .OrderByDescending(g => g.Count()))
			Avisar(pl, $"  {g.Key}: {g.Count()}");

		Avisar(pl, $"  você: {QuantasSupersTem(pl.Assinatura)}/{SuperEsferas.Total}");

		// AS DUAS LINHAS FINAIS DO VERB DO DM (`:1699-1700`), e as duas sao informacao que nao existe em
		// nenhum outro lugar da tela: se voce fala a lingua, e se ha uma procuracao de pe.
		ConferirALingua(pl);
		Avisar(pl, pl.Ficha.godtongue
			? "  você FALA a língua dos deuses."
			: "  você NÃO fala a língua dos deuses (cargos divinos falam, e o sangue Kai ou Demigod "
			+ "nasce com ela) -- ou empreste as sete a quem fale, com sdb_transferir.");

		if (HaProcuracao)
			Avisar(pl, $"  guarda emprestada: o desejo será de {_benefNome}"
					 + (_pedidoDoBenef.Length > 0
						? $", e o pedido dele(a) já está registrado ({_pedidoDoBenef})."
						: ", e ele(a) ainda não registrou o pedido."));

		Avisar(pl, "  elas ficam no ESPAÇO, do tamanho de um planetoide. Não se carregam -- "
				 + "reivindique-as chegando perto.");
	}
}
