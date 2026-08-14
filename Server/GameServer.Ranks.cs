using Godot;
using Jandirus.Core.Ranks;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// OS CARGOS DO MUNDO. Quem e o Guardiao da Terra, quem e o Kaio do Norte, quem e o Kaioshin.
///
/// SAO DO SERVIDOR, NAO DO PERSONAGEM: um cargo tem UM dono no mundo inteiro, e por isso ele
/// mora aqui e nao no save de ninguem. O que o save do personagem guarda e so a CHAVE do cargo
/// que ele carrega -- e mesmo essa e reconferida contra a lista do mundo ao entrar, senao dois
/// jogadores voltariam de logout se dizendo Kaioshin.
///
/// A CHAVE E A CONTA, e isso e uma licao do original: la o sistema comparava ora por `key`, ora
/// por `name`, ora por `signature`, e o resultado era rank que nao transferia e trono que nunca
/// vagava. Aqui e SEMPRE a conta -- o unico identificador que nao muda e nao se repete.
/// </summary>
public partial class GameServer
{
	/// <summary>chave do cargo -> conta do dono. Vazio = trono vago.</summary>
	private readonly Dictionary<string, string> _tronos = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Ao lado dos saves de conta. Um arquivo de texto de uma dezena de linhas -- cargo e
	/// coisa RARA, e um formato que se le num editor vale mais aqui que economia de bytes.
	/// </summary>
	// ============================ PELO `_store`, E NAO PELO CAMINHO CRAVADO ============================
	// Este arquivo (e mais tres: `mestres.txt`, `herdeiros.txt`, `titulo.txt`) montava o caminho com
	// `GlobalizePath("user://saves")` escrito na mao, enquanto os oito `.json` do mundo perguntam ao
	// `_store.Pasta`. Sao a mesma pasta hoje -- e por isso a divergencia nunca deu sintoma.
	//
	// Ela vira defeito no dia em que a pasta se mover: uma bancada com pasta propria, ou o dono
	// rodando dois servidores na mesma maquina. Ai estes quatro continuariam apontando pro lugar
	// antigo enquanto todo o resto do mundo mudaria de casa -- e a LIMPEZA TOTAL, que apaga a pasta do
	// `_store`, deixaria os tronos, o discipulado e a sucessao inteiros para tras. E exatamente o
	// "apagou de menos" que este trabalho existe pra evitar, escrito em quatro linhas de infra.
	//
	// O `??` continua cravando o caminho pra o caso de nao haver `_store`: nao ha, hoje, mas devolver
	// vazio faria o `Path.Combine` gravar na pasta de trabalho do processo.
	// ==============================================================================================
	private string ArquivoDeCargos =>
		System.IO.Path.Combine(_store?.Pasta ?? ProjectSettings.GlobalizePath("user://saves"), "cargos.txt");

	private void CarregarCargos()
	{
		_tronos.Clear();
		if (!System.IO.File.Exists(ArquivoDeCargos)) return;
		foreach (string linha in System.IO.File.ReadAllLines(ArquivoDeCargos))
		{
			string[] p = linha.Split('\t');
			if (p.Length == 2 && p[1].Length > 0) _tronos[p[0]] = p[1];
		}
		GD.Print($"[server] cargos ocupados: {_tronos.Count} de {Cargos.Todos.Length}");
	}

	private void SalvarCargos()
	{
		try
		{
			System.IO.File.WriteAllLines(ArquivoDeCargos,
				_tronos.Select(kv => $"{kv.Key}\t{kv.Value}"));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei os cargos: {e.Message}"); }
	}

	/// <summary>Qual cargo esta conta carrega ("" = nenhum).</summary>
	private string CargoDe(string conta) =>
		_tronos.FirstOrDefault(kv => string.Equals(kv.Value, conta, StringComparison.OrdinalIgnoreCase)).Key ?? "";

	private FichaDeRank Ficha(ServerPlayer pl) => new()
	{
		Raca = pl.Race,
		Classe = pl.Class,
		BpBase = pl.Ficha.BP,
		Karma = pl.Karma,
		CargoAtual = CargoDe(pl.Conta),

		// OS QUATRO CAMPOS NOVOS. Sem preencher aqui, todo requisito que depende deles passava
		// de graca (bool false e double 0 sao os defaults, e a comparacao e ">=") -- os cargos de
		// Kaio, que o DM gateia por God Ki desperto, abririam pra qualquer um.
		RacaMae = pl.Ficha.Genoma?.MajorityRace ?? pl.Race,
		GodKiDesperto = pl.Ficha.godki?.awakened == true,
		GodKiMaestria = pl.Ficha.godki?.mastery ?? 0,
		Zeni = pl.Ficha.Zeni,

		// ============================ OS DOIS CAMPOS QUE A CONQUISTA TROUXE ============================
		// Quatro cargos carregavam a pendencia *"o port nao tem reputacao de planeta"* e ela caducou --
		// o livro-caixa existe desde a cadeia de sagas e ganhou duas fontes novas (matar habitante,
		// conquistar). O que faltava era CHEGAR aqui: regra escrita nao e regra ligada.
		//
		// O mapa e montado por planeta com povo, que sao os unicos que o DM cita nos requisitos. Nao ha
		// atalho lendo "a reputacao do jogador": ela e por planeta, e um escalar responderia a pergunta
		// errada com um numero plausivel.
		ReputacaoDePlaneta = ReputacaoDe(pl.Conta),
		PlanetasDominados = QuantosDomina(pl.Assinatura),
	};

	/// <summary>
	/// REIVINDICAR. Tres portas, nesta ordem, e a ordem decide a mensagem que o jogador le:
	/// o trono precisa estar VAGO, a alma nao pode carregar outro cargo, e os requisitos tem
	/// que estar cumpridos.
	/// </summary>
	private void ReivindicarCargo(ServerPlayer pl, string chave)
	{
		RankDef? r = Cargos.Get(chave);
		if (r == null) { Avisar(pl, "esse cargo nao existe."); return; }

		if (_tronos.TryGetValue(r.Chave, out string? dono) && dono.Length > 0)
		{
			Avisar(pl, string.Equals(dono, pl.Conta, StringComparison.OrdinalIgnoreCase)
				? $"voce JA e {r.Nome}."
				: $"{r.Nome} ja tem dono.");
			return;
		}

		// UMA ALMA, UM TRONO. A escada e a excecao: subir de Kaio a Grande Kaio LARGA o cargo
		// anterior em vez de acumular -- senao o mundo perderia um Kaio cardeal a cada promocao.
		string atual = CargoDe(pl.Conta);
		bool subindo = atual.Length > 0 && r.Degraus.Contains(atual, StringComparer.OrdinalIgnoreCase);
		if (atual.Length > 0 && !subindo)
		{
			Avisar(pl, $"voce ja carrega {Cargos.Get(atual)?.Nome ?? atual}. Uma alma, um trono.");
			return;
		}

		// ============================ O PEDAGIO DA ASCENSAO E O RENOME ============================
		// `RQ_PROMO_QUESTS 3` (`RankQuests.dm:53`, cobrado em `:292`): so sobe quem SERVIU no cargo de
		// agora. Enquanto o motor de tarefas nao existia, esta linha era uma pendencia escrita no
		// Grand Kai e no Kaioshin (*"o port nao tem o motor de tarefas"*) e a escada dos Kaios subia so
		// com o requisito -- mais barata que o original, e a divida mais cara do `Ranks.cs`.
		//
		// O RENOME E DE QUEM CARREGA O CARGO AGORA, e nao do trono: ver `RenomeDe`. Herdar o servico de
		// quem veio antes seria comprar promocao com o trabalho alheio.
		// ======================================================================================
		if (subindo && !MissoesDeCargo.PodeAscender(RenomeDe(atual)))
		{
			int tem = RenomeDe(atual);
			Avisar(pl, $"ainda falta RENOME para almejar algo maior: {tem}/{MissoesDeCargo.TarefasParaAscender} "
					 + $"tarefas cumpridas como {Cargos.Get(atual)?.Nome ?? atual}. Sirva ao cargo primeiro "
					 + "(verb Rank Duty).");
			return;
		}

		if (Cargos.OqueFalta(r, Ficha(pl)) is { } falta)
		{
			Avisar(pl, $"{r.Nome} exige: {falta}.");
			return;
		}

		if (subindo) _tronos.Remove(atual);
		_tronos[r.Chave] = pl.Conta;
		SalvarCargos();

		GD.Print($"[server] {pl.Name} ({pl.Conta}) assumiu o cargo '{r.Nome}'"
				 + (subindo ? $" (largou {atual})" : ""));

		AnunciarCargo(r, pl, subindo ? atual : "");
	}

	/// <summary>
	/// PoE ALGUEM NO TRONO IGNORANDO OS REQUISITOS. E o `Give (Rank)` do original, e a unica
	/// porta de entrada que a reivindicacao nao cobre.
	///
	/// Existe porque cargo, hoje, so se consegue cumprindo requisito -- e isso deixa sem conserto
	/// o caso em que ele TRAVOU: o dono abandonou o jogo, ou o requisito mudou depois de alguem ja
	/// ter assumido. Sem admin, o trono fica preso pra sempre.
	///
	/// O QUE ELE NAO IGNORA: "uma alma, um trono" continua valendo dos dois lados -- quem recebe
	/// larga o que tinha, e quem estava no trono sai dele.
	/// </summary>
	private void Outorgar(RankDef r, ServerPlayer pl)
	{
		string largou = CargoDe(pl.Conta);
		if (largou.Length > 0) _tronos.Remove(largou);

		if (_tronos.TryGetValue(r.Chave, out string? antigo) && antigo.Length > 0
			&& !string.Equals(antigo, pl.Conta, StringComparison.OrdinalIgnoreCase))
		{
			foreach (ServerPlayer o in _players.Values)
				if (string.Equals(o.Conta, antigo, StringComparison.OrdinalIgnoreCase))
					Avisar(o, $"voce nao e mais {r.Nome}.");
		}

		_tronos[r.Chave] = pl.Conta;
		SalvarCargos();
		GD.Print($"[server] {pl.Name} ({pl.Conta}) recebeu o cargo '{r.Nome}' por outorga de admin");
		AnunciarCargo(r, pl, largou);
	}

	/// <summary>
	/// O MUNDO INTEIRO FICA SABENDO. Cargo e posicao social: se ninguem soube, nao houve.
	/// Um lugar so porque a reivindicacao e a outorga precisam anunciar igual.
	/// </summary>
	private void AnunciarCargo(RankDef r, ServerPlayer pl, string largou)
	{
		foreach (ServerPlayer o in _players.Values)
		{
			// `falante: null` -- e o servidor anunciando pro mundo inteiro, e nao alguem falando
			// perto de voce: ninguem fica mais familiar por ter lido a outorga de um cargo.
			Mandar(o, Protocol.Fala.Sistema, "",
				o == pl ? $"VOCE E O NOVO {r.Nome.ToUpperInvariant()}."
						: $"{pl.Name} assumiu o cargo de {r.Nome}.",
				falante: null);

			// E A LISTA VAI JUNTO. O cliente so pede a lista de cargos quando ela esta VAZIA -- ou
			// seja, quem ja tinha aberto a aba continuaria lendo o dono antigo ate relogar. A
			// reivindicacao escapava disso por acidente (o handler do pedido responde logo depois);
			// a outorga de admin nao tinha ninguem pra responder por ela.
			MandarCargos(o);
		}
		if (largou.Length > 0) Avisar(pl, $"voce largou {Cargos.Get(largou)?.Nome ?? largou}.");
	}

	/// <summary>A lista de cargos e quem os ocupa -- e o que a aba mostra.</summary>
	private void MandarCargos(ServerPlayer pl)
	{
		var w = Protocol.Begin(Protocol.S2C.Cargos);
		w.Put((byte)Cargos.Todos.Length);
		FichaDeRank f = Ficha(pl);
		foreach (RankDef r in Cargos.Todos)
		{
			w.Put(r.Chave);
			_tronos.TryGetValue(r.Chave, out string? dono);
			w.Put(NomeDoDono(dono));
			// o que falta PRA MIM: e a unica parte que muda de jogador pra jogador
			w.Put(Cargos.OqueFalta(r, f) ?? "");
		}
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O NOME de quem ocupa, e nao a conta. A conta e a chave interna; mostrar "roboA" onde
	/// deveria estar "Goku" entrega o login de quem joga.
	/// </summary>
	private string NomeDoDono(string? conta)
	{
		if (string.IsNullOrEmpty(conta)) return "";
		ServerPlayer? vivo = _players.Values.FirstOrDefault(
			p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase));
		if (vivo != null) return vivo.Name;

		// offline: nao vamos abrir save pra desenhar uma lista. A conta serve de rotulo ate
		// a pessoa entrar -- e cargo e de conta, nao de personagem.
		return conta;
	}
}
