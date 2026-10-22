using Godot;
using Jandirus.Core.Npc;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-18) AS SAGAS PELA ABA ADMIN -- o dono: *"adicione um verb pra adm q permite ele resetar uma saga, pra eu poder resetar a do freeza de
/// namek ja q ele morreu antes da hora"*.
///
/// `admin_sagas` responde a lista da cadeia pelas ESCOLHAS (`S2C.Escolhas`, a mesma resposta do menu da tecla E): uma linha por saga, "id|nome
/// -- estado", e o painel da aba Admin desenha o botao de cada uma (o cliente nao le o `npcs.json`). `admin_saga_resetar id` recomeca a saga DO
/// MARCO: os chefes dela que estao no mundo saem, e o mesmo `DispararElo` de producao a arma de novo -- a contagem em dias in-game volta ao
/// comeco e os chefes chegam com o BP da media de agora, na forma 1. As outras sagas da cadeia ficam como estao; o admin le o estado delas na
/// mesma lista.
///
/// (2026-10-18) `admin_saga_agora id` PULA OS DIAS DE AVISO -- o pedido do dono: o admin poder pular os dias de aviso do chefe de saga, e ele
/// nascer agora. Os chefes A CAMINHO daquela saga chegam ja, pelo MESMO nascimento da virada do dia (`VirouODia` -> `NascerChefeDaSaga`: o BP
/// pinado, o letal, a cena da chegada pra todos). A saga que nao esta contando (adormecida, ou ja acabada) dispara do marco antes, pelo
/// `DispararElo` do Resetar -- e a contagem que ele arma e a que se pula. Quem ja esta no mundo fica como esta.
///
/// (2026-10-20) `admin_saga_pular id` PULA A SAGA JA COM O CHEFE MORTO -- o dono: *"adicione um botao q pula uma saga ja matando o boss, pq sem
/// querer resetei todas as sagas do server"*. Ela fecha VENCIDA, como se os jogadores a tivessem vencido. Ver <see cref="AdminPularSaga"/>.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (`--chefesteste`, familia P): o verbo zera a contagem e espera a VIRADA do dia pra o chefe nascer -- o "pular os dias" que
	/// ainda deixa o admin esperando ate 24 minutos. Lido pelo <see cref="AdminSagaAgora"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool AgoraEsperaAViradaDeTeste;

	/// <summary>A lista das sagas pro painel -- e, no chat, o estado de cada uma.</summary>
	private void AdminSagas(ServerPlayer pl)
	{
		var itens = new List<string>();
		foreach (EloDaSaga elo in _cadeia)
		{
			string linha = $"{elo.Nome} -- {DescreverElo(_sagas.Do(elo.Id))}";
			itens.Add($"{elo.Id}|{linha}");
			Avisar(pl, $"  saga {elo.Id}: {linha}");
		}
		if (itens.Count == 0) Avisar(pl, "a cadeia de sagas esta vazia (o `npcs.json` nao tem nenhuma).");
		MandarEscolhas(pl, "admin_sagas", itens);
	}

	/// <summary>RESETA UMA SAGA: tira os chefes dela do mundo e a dispara de novo, do marco. Ver o cabecalho.</summary>
	private void AdminResetarSaga(ServerPlayer pl, string arg)
	{
		string id = arg.Trim();
		EloDaSaga? elo = Array.Find(_cadeia, e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
		if (elo == null)
		{
			Avisar(pl, $"não há saga \"{id}\". As da cadeia: {string.Join(", ", _cadeia.Select(e => e.Id))}.");
			return;
		}

		EstadoDoElo? est = _sagas.Do(elo.Id);
		if (est == null)
		{
			est = new EstadoDoElo { Id = elo.Id };
			_sagas.Elos.Add(est);
		}
		string antes = DescreverElo(est);

		// OS CHEFES DESTE ELO QUE ESTAO NO MUNDO SAEM DELE: a saga recomeca do marco, e eles chegam de novo pela contagem.
		int tirados = 0;
		foreach (EstadoDoChefe ec in est.Chefes)
			if (ec.Corpo != 0 && _players.TryGetValue(ec.Corpo, out ServerPlayer? corpo) && corpo.Peer == null)
			{
				RemoverNpc(corpo);
				tirados++;
			}

		est.Condenado = false;
		DispararElo(elo, est, $"admin {pl.Name}", 0);
		SalvarSagas();
		Avisar(pl, $"saga \"{elo.Nome}\" resetada. Era: {antes}. Agora: {DescreverElo(est)}"
				   + (tirados > 0 ? $" ({tirados} chefe(s) tirado(s) do mundo)." : "."));
		GD.Print($"[server] ADMIN {pl.Name} resetou a saga '{elo.Id}' (era: {antes})");
	}

	/// <summary>OS CHEFES DE UMA SAGA CHEGAM AGORA: os dias de aviso sao pulados. Ver o cabecalho.</summary>
	private void AdminSagaAgora(ServerPlayer pl, string arg)
	{
		string id = arg.Trim();
		EloDaSaga? elo = Array.Find(_cadeia, e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
		if (elo == null)
		{
			Avisar(pl, $"não há saga \"{id}\". As da cadeia: {string.Join(", ", _cadeia.Select(e => e.Id))}.");
			return;
		}

		EstadoDoElo? est = _sagas.Do(elo.Id);
		if (est == null)
		{
			est = new EstadoDoElo { Id = elo.Id };
			_sagas.Elos.Add(est);
		}
		string antes = DescreverElo(est);

		// A SAGA QUE NAO ESTA CORRENDO dispara do marco primeiro -- adormecida, ou ja acabada (vencida, consumada, cancelada). Sem isto o
		// botao numa saga adormecida nao teria contagem nenhuma pra pular, e o admin que quer o Freeza agora teria que apertar dois botoes.
		if (!est.Disparou || est.Fase is FaseDaSaga.Adormecida || Sagas.Terminou(est.Fase))
		{
			est.Condenado = false;
			DispararElo(elo, est, $"admin {pl.Name}", 0);
		}

		int chegam = 0;
		foreach (EstadoDoChefe ec in est.Chefes)
			if (ec.Fase == FaseDoChefe.ACaminho) { ec.DiasRestantes = 0; chegam++; }

		// AS CHEGADAS DE HOJE, pela virada de producao: e ela que poe no mundo quem tem zero dias (`NascerChefeDaSaga`). Os chefes das OUTRAS
		// sagas nao andam um dia por causa deste botao -- e o mesmo `soAsChegadasDeHoje` do disparo, que nao desconta nada.
		if (chegam > 0 && !AgoraEsperaAViradaDeTeste) VirouODia(soAsChegadasDeHoje: true);
		SalvarSagas();

		Avisar(pl, (chegam > 0 ? $"saga \"{elo.Nome}\": {chegam} chefe(s) chegando agora." : $"saga \"{elo.Nome}\": nenhum chefe a caminho -- quem é dela já está no mundo.")
				 + $" Era: {antes}. Agora: {DescreverElo(est)}.");
		GD.Print($"[server] ADMIN {pl.Name} pulou os dias da saga '{elo.Id}' ({chegam} chefe(s); era: {antes})");
	}

	/// <summary>
	/// PULA UMA SAGA, JA COM O CHEFE MORTO: ela fecha VENCIDA, como se os jogadores a tivessem vencido. Os chefes dela que estao no mundo saem (a
	/// esfera do ultimato no ar se desfaz antes -- a tela leria a saida do corpo como a esfera caindo), os a caminho nao chegam mais e quem ia
	/// voltar do alem (o Cell Super Perfeito) nao volta: todos ficam DERROTADOS, cada um na forma em que estava. A saga que nunca disparou (o
	/// marco nao batido) fecha do mesmo jeito, com os chefes da ficha derrotados na forma 1.
	///
	/// O RESTO DA VITORIA VEM DO ESTADO, sem codigo novo: o elo seguinte fica destravado (dispara pelo marco dele, como sempre), os derrotados
	/// viram candidatos a vilao do Inferno (`ViloesDoInferno.Candidatos`), o Boo vencido acorda a historia do Bills, e a tropa, os Cell Jr e o
	/// torneio do Cell vao embora no tique seguinte (eles perguntam pelo chefe no mundo e pela saga em curso). NINGUEM vira heroi -- nao houve
	/// matador -- e o mundo nao ouve o anuncio da vitoria: e uma correcao de admin, e quem joga nao viu luta nenhuma. O admin ouve o que mudou.
	/// </summary>
	private void AdminPularSaga(ServerPlayer pl, string arg)
	{
		string id = arg.Trim();
		EloDaSaga? elo = Array.Find(_cadeia, e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
		if (elo == null)
		{
			Avisar(pl, $"não há saga \"{id}\". As da cadeia: {string.Join(", ", _cadeia.Select(e => e.Id))}.");
			return;
		}

		EstadoDoElo? est = _sagas.Do(elo.Id);
		if (est == null)
		{
			est = new EstadoDoElo { Id = elo.Id };
			_sagas.Elos.Add(est);
		}
		string antes = DescreverElo(est);

		int mortos = 0;
		if (!PularSoAFaseDeTeste)
		{
			// OS CHEFES DA FICHA QUE A SAGA NUNCA POS NO MUNDO (ela nao disparou): entram derrotados, na forma 1
			foreach (ChefeDaSaga ficha in elo.Chefes)
				if (!est.Chefes.Exists(c => string.Equals(c.Molde, ficha.Molde, StringComparison.OrdinalIgnoreCase)))
					est.Chefes.Add(new EstadoDoChefe { Molde = ficha.Molde, Fase = FaseDoChefe.Derrotado });

			foreach (EstadoDoChefe ec in est.Chefes)
			{
				if (ec.Corpo != 0 && _players.TryGetValue(ec.Corpo, out ServerPlayer? corpo) && corpo.Peer == null)
				{
					AcabarASupernova(corpo);
					RemoverNpc(corpo);
					mortos++;
				}
				if (ec.Fase != FaseDoChefe.Consumido) ec.Fase = FaseDoChefe.Derrotado;
				ec.RessurgeEm = 0;
				ec.Corpo = 0;
				ec.Carregando = false;
				ec.UltimatoEm = 0;
			}
		}
		est.Disparou = true;
		est.Condenado = false;
		est.Fase = FaseDaSaga.Vencida;
		SalvarSagas();

		Avisar(pl, $"saga \"{elo.Nome}\" pulada: vencida" + (mortos > 0 ? $", {mortos} chefe(s) morto(s) e tirado(s) do mundo" : "")
				 + $". Era: {antes}. Agora: {DescreverElo(est)}.");
		GD.Print($"[server] ADMIN {pl.Name} pulou a saga '{elo.Id}': vencida, {mortos} chefe(s) tirado(s) do mundo (era: {antes})");
	}

	/// <summary>DEFEITO INJETADO (`--chefesteste`, familia V): o pular so troca a fase -- os chefes ficam no mundo e o Cell segue armado pra voltar.</summary>
	internal static bool PularSoAFaseDeTeste;

	/// <summary>O estado de uma saga em palavras -- a fase e, por chefe, onde ele esta e em que forma.</summary>
	private static string DescreverElo(EstadoDoElo? est)
	{
		if (est == null || (!est.Disparou && est.Fase == FaseDaSaga.Adormecida)) return "adormecida (ninguém bateu o marco)";
		string fase = est.Fase switch
		{
			FaseDaSaga.Adormecida => "adormecida",
			FaseDaSaga.Contando => "contando os dias",
			FaseDaSaga.EmCurso => "em curso",
			FaseDaSaga.Vencida => "vencida",
			FaseDaSaga.Consumada => "consumada",
			FaseDaSaga.Cancelada => "cancelada",
			_ => est.Fase.ToString(),
		};
		string chefes = string.Join(", ", est.Chefes.Select(c => c.Fase switch
		{
			FaseDoChefe.ACaminho => $"{c.Molde} a caminho ({c.DiasRestantes} dia(s))",
			FaseDoChefe.NoMundo => $"{c.Molde} no mundo, forma {c.Degrau + 1}",
			FaseDoChefe.Derrotado => $"{c.Molde} derrotado na forma {c.Degrau + 1}",
			FaseDoChefe.Consumido => $"{c.Molde} absorvido",
			_ => $"{c.Molde} {c.Fase}",
		}));
		return chefes.Length > 0 ? $"{fase}: {chefes}" : fase;
	}
}
