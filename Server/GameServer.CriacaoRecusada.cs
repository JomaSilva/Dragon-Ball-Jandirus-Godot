using Godot;
using Jandirus.Core.Npc;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ A CRIACAO QUE ESTOURA RESPONDE (2026-10-23) ============================
/// O relato do dono: *"alguns jogadores caem em uma tela cinza na criaçao"*. Uma das portas pra tela cinza era esta: QUALQUER
/// EXCECAO no `CreateChar` subia ate o `NetworkReceiveEvent` (`Wire`), virava um aviso de "pacote invalido" no console do servidor e
/// mais nada -- o cliente, que tinha subido a cobertura no "Entrar no mundo", ficava em "preparando o mundo..." pra sempre, sem
/// resposta nenhuma pra ler.
///
/// Agora a criacao passa por <see cref="CriarOuRecusar"/>: a excecao vai pro log do servidor INTEIRA (a conta, o endereco, a pilha) e
/// quem mandou a ficha recebe uma recusa de motivo generico (<see cref="MotivoDoErroNaCriacao"/>) -- e o cliente reabre a criacao com
/// ele (`Boot.RecusaDaCriacao.cs`). O erro em si continua sendo um defeito a consertar; isto so garante que ele nao prende ninguem.
/// (2026-10-24, o dono: "Consertar") O "Jogar" de quem ja tem personagem passa pela mesma porta (<see cref="EntrarOuRecusar"/>), e a
/// selecao mostra o motivo.
/// ====================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O MOTIVO QUE O JOGADOR LE quando a criacao estoura no servidor. Generico de proposito: a mensagem da excecao e do servidor (vai
	/// pro log), e nao diz nada a quem joga. Cabe no fio da recusa (`GetString(120)` no cliente). Tem a linha em ingles no
	/// `Assets/Data/lingua-en.json`.
	/// </summary>
	internal const string MotivoDoErroNaCriacao = "erro no servidor ao criar o personagem -- tente de novo (o erro ficou no log do servidor)";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--telacinzateste`): a excecao da criacao volta a ser so um aviso no log, sem resposta -- o jogo de
	/// antes, o cliente preso na cobertura. Lido pelo <see cref="CriarOuRecusar"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool ErroDaCriacaoCaladoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--telacinzateste`): a excecao com o corpo JA no mundo tambem e recusada -- a recusa chegaria depois do
	/// `JoinAccepted`, e a criacao reabriria por cima do mundo. Lido pelo <see cref="CriarOuRecusar"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool RecusaDepoisDeEntrarDeTeste;

	/// <summary>
	/// PALCO (bancada `--telacinzateste`, robo `--diagtelacinza`): a criacao ESTOURA -- antes de nascer (<see cref="CriacaoEstouraDeTeste"/>)
	/// ou com o corpo ja no mundo (<see cref="CriacaoEstouraDepoisDeEntrarDeTeste"/>). Nao e defeito do jogo: e a reproducao forcada da
	/// excecao que ninguem consegue provocar de proposito. Falsos em jogo, sempre.
	/// </summary>
	internal static bool CriacaoEstouraDeTeste, CriacaoEstouraDepoisDeEntrarDeTeste;

	/// <summary>A ultima excecao da criacao que foi pro log (a mensagem inteira), pra bancada conferir que o erro ficou escrito.</summary>
	internal string UltimoErroDaCriacao { get; private set; } = "";

	/// <summary>
	/// O `C2S.CreateChar` INTEIRO -- a leitura do pacote e o `CreateChar` -- dentro de um `try`. A leitura entra junto: um pacote que nao
	/// se le tambem deixaria o cliente na cobertura.
	/// </summary>
	private void CriarOuRecusar(NetPeer peer, NetPacketReader reader)
	{
		try
		{
			int slot = reader.GetByte();
			Jandirus.Core.Races.CharacterDraft ficha = reader.GetDraft();
			Jandirus.Core.Appearance.Appearance visual = reader.GetAppearance();
			if (CriacaoEstouraDeTeste) throw new InvalidOperationException("(bancada) a criacao estourou de proposito, antes de o personagem nascer");
			CreateChar(peer, slot, ficha, visual);
			if (CriacaoEstouraDepoisDeEntrarDeTeste) throw new InvalidOperationException("(bancada) a criacao estourou de proposito, com o corpo ja no mundo");
		}
		catch (Exception ex)
		{
			UltimoErroDaCriacao = ex.ToString();
			GD.PushError($"[server] ERRO NA CRIACAO DE PERSONAGEM (conta '{ContaDoPar(peer)}', de {peer.Address}) -- o jogador recebe a recusa generica: {ex}");

			// QUEM JA ENTROU NO MUNDO NAO E RECUSADO: o `JoinAccepted` ja saiu (o `Entrar` registra o par no `_byPeer` logo antes dele), e
			// uma recusa depois dele reabriria a criacao por cima do mundo. O erro fica so no log.
			if ((_byPeer.ContainsKey(peer) && !RecusaDepoisDeEntrarDeTeste) || ErroDaCriacaoCaladoDeTeste) return;
			Recusar(peer, MotivoDoErroNaCriacao);

			// E A LISTA DE VAGAS DE AGORA, como a recusa do bebe que ja nasceu: o estouro pode ter vindo depois de a vaga ser gravada, e a
			// selecao por baixo da criacao reaberta tem de mostrar o que o servidor tem. Quem saiu do `_logados` (o `Entrar` ja o tirou)
			// nao tem lista -- a recusa basta.
			if (_logados.TryGetValue(peer, out AccountSave? acc))
			{
				try { MandarSlots(peer, acc); }
				catch (Exception ex2) { GD.PushError($"[server] a lista de vagas depois do erro na criacao tambem estourou: {ex2.Message}"); }
			}
		}
	}

	/// <summary>A conta deste par -- logada (na selecao) ou em jogo --, pro log do erro.</summary>
	private string ContaDoPar(NetPeer peer) =>
		_logados.TryGetValue(peer, out AccountSave? logada) ? logada.Conta
		: _contas.TryGetValue(peer, out AccountSave? emJogo) ? emJogo.Conta : "?";

	// =====================================================================
	// (2026-10-24) O "JOGAR" TAMBEM RESPONDE -- o dono: "Consertar"
	// =====================================================================
	/// <summary>
	/// O MOTIVO QUE O JOGADOR LE quando o "Jogar" estoura no servidor (o `Entrar` de quem ja tem personagem). Generico pelo mesmo motivo
	/// do da criacao, e com a linha em ingles no `Assets/Data/lingua-en.json`.
	/// </summary>
	internal const string MotivoDoErroAoEntrar = "erro no servidor ao entrar no mundo -- tente de novo (o erro ficou no log do servidor)";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--telacinzateste`): a excecao do "Jogar" volta a ser so um aviso no log, sem resposta -- o jogo de antes,
	/// o jogador preso na cobertura. Lido pelo <see cref="EntrarOuRecusar"/>. Falso em jogo, sempre.
	/// </summary>
	internal static bool ErroDoJogarCaladoDeTeste;

	/// <summary>
	/// PALCO (bancada `--telacinzateste`, robo `--diagtelacinza --jogar`): o "Jogar" ESTOURA -- antes de entrar
	/// (<see cref="JogarEstouraDeTeste"/>) ou com o corpo ja no mundo (<see cref="JogarEstouraDepoisDeEntrarDeTeste"/>). A reproducao
	/// forcada, como a da criacao. Falsos em jogo, sempre.
	/// </summary>
	internal static bool JogarEstouraDeTeste, JogarEstouraDepoisDeEntrarDeTeste;

	/// <summary>A ultima excecao do "Jogar" que foi pro log (a mensagem inteira), pra bancada conferir que o erro ficou escrito.</summary>
	internal string UltimoErroDoJogar { get; private set; } = "";

	/// <summary>
	/// O `C2S.PickSlot` -- o "Jogar" -- dentro de um `try`, como a criacao. QUALQUER EXCECAO no `Entrar` (o funil que monta o corpo de
	/// quem ja existe) deixava o cliente na cobertura pra sempre -- e o motivo que viesse ia pro rotulo escondido do login. Agora o erro
	/// vai inteiro pro log e quem apertou "Jogar" recebe o motivo generico, que a selecao mostra (`CharacterSelect.MostrarARecusa`). Quem
	/// ja entrou nao e recusado (o mesmo corte da criacao). Sem lista de vagas de volta: o "Jogar" nao muda vaga nenhuma.
	/// </summary>
	private void EntrarOuRecusar(NetPeer peer, NetPacketReader reader)
	{
		try
		{
			int slot = reader.GetByte();
			if (JogarEstouraDeTeste) throw new InvalidOperationException("(bancada) o Jogar estourou de proposito, antes de entrar");
			PickSlot(peer, slot);
			if (JogarEstouraDepoisDeEntrarDeTeste) throw new InvalidOperationException("(bancada) o Jogar estourou de proposito, com o corpo ja no mundo");
		}
		catch (Exception ex)
		{
			UltimoErroDoJogar = ex.ToString();
			GD.PushError($"[server] ERRO NO JOGAR (conta '{ContaDoPar(peer)}', de {peer.Address}) -- o jogador recebe a recusa generica: {ex}");
			if (JaEstaNoMundo(peer) || ErroDoJogarCaladoDeTeste) return;
			Recusar(peer, MotivoDoErroAoEntrar);
		}
	}

	/// <summary>
	/// O par ja tem corpo no mundo? O `Entrar` o registra no `_byPeer` logo antes do `JoinAccepted`: dali pra frente uma recusa chegaria
	/// por cima do mundo. O botao de defeito e o mesmo da criacao (<see cref="RecusaDepoisDeEntrarDeTeste"/>).
	/// </summary>
	private bool JaEstaNoMundo(NetPeer peer) => _byPeer.ContainsKey(peer) && !RecusaDepoisDeEntrarDeTeste;

	/// <summary>
	/// PALCO (robo `--diagtelacinza --jogar`): um personagem na vaga 1 desta conta, criado PELO FIO -- um cliente cru entra, cria, e sai
	/// (o `Drop` grava). O robo precisa de alguem que ja existe pra apertar "Jogar", e o caminho da criacao do jogo termina no mundo.
	/// Devolve o nome de quem esta na vaga 1 (o de antes, se ja havia), ou vazio se nao deu.
	/// </summary>
	internal string PersonagemPeloFioDeTeste(int porta, string conta, string senha, string nome)
	{
		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		try
		{
			if (!mesa.Entra(cli)) return "";
			cli.Login(conta, senha);
			if (!mesa.Bombeia(() => cli.Slots != null || cli.Recusa != null, 4000) || cli.Slots is not { Count: > 0 } vagas) return "";
			if (vagas[0].Ocupado) return vagas[0].Nome;
			cli.CriarPersonagem(0, FichaDeClienteCru(nome));
			if (!mesa.Bombeia(() => cli.NoMundo || cli.Recusa != null, 8000) || !cli.NoMundo) return "";
			return mesa.SaiDoJogo(cli, conta) ? nome : "";
		}
		finally { mesa.Fecha(); }
	}

	/// <summary>
	/// PALCO (robo `--diagtelacinza`): o nome de um MORADOR do mundo que esta no `_players` -- um cidadao do povoamento, de nome que a
	/// criacao aceitaria (2 a 24 letras). Vazio enquanto o povoamento nao trouxe ninguem.
	/// </summary>
	internal string NomeDeUmMoradorDeTeste() =>
		_players.Values
			.Where(p => EhNpcDoMundo(p) && p.Papel is { Molde.Tipo: TipoDeNpc.Cidadao } && !p.Ficha.dead
						&& p.Name == p.Name.Trim() && p.Name.Length is >= 2 and <= 24)
			.Select(p => p.Name)
			.OrderBy(n => n, StringComparer.Ordinal)
			.FirstOrDefault() ?? "";

	/// <summary>PALCO (robo `--diagtelacinza`): o personagem desta conta esta no mundo? Com que nome?</summary>
	internal string? NoMundoDeTeste(string conta) =>
		Jogadores.FirstOrDefault(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase))?.Name;

	/// <summary>
	/// PALCO (robo `--diagtelacinza`): a fila de bebes VAZIA, antes de o robo por o dele -- a pasta de saves da bancada e a mesma de uma
	/// rodada pra outra, e a oferta da lista e a do bebe mais antigo.
	/// </summary>
	internal void EsvaziarAFilaDeBebesDeTeste()
	{
		_bebes = new BebesNoDisco();
		SalvarBebes();
	}
}
