using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.Verbos;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// **OS FATOS DE QUEM JOGA** -- o que so o servidor sabe e que decide quais VERBS existem pra cada um.
///
/// ============================ O QUE ISTO RESOLVE ============================
/// O dono (2026-10-10): *"seria melhor dar uma limpa em verbs q sao exclusivas de raças e deveriam so
/// aparecer se o player tem os requisitos se nao fica mt poluido"*. Raca, classe, livro, mochila e cargos
/// o cliente ja recebia. O resto -- se este androide tem coletores, se ha um convite de fusao na mesa, se
/// este corpo guia um aluno, se a bandeira deste planeta e dele -- so existia AQUI, em quinze dicionarios
/// de quinze sistemas, e a tela nao tinha como saber: mostrava o botao pra todo mundo e deixava o verbo
/// recusar.
///
/// A regra de visibilidade e uma so e mora no Core (`Core/Verbos/RequisitosDosVerbos.cs`); este arquivo e
/// so o que faltava pra ela poder ser respondida do outro lado do fio.
///
/// ============================ LEITURA, E NAO OUVINTE ============================
/// Os fatos sao LIDOS do estado, cinco vezes por segundo, e saem so quando mudam. A alternativa era um
/// aviso em cada porta que os muda: o convite que nasce, o que e aceito, o que vence pelo relogio (ninguem
/// "faz" um convite vencer -- ele so deixa de valer), o trono que troca, a bandeira que cai, a copia que
/// morre. Sao dezenas de portas em quinze arquivos, e a proxima que nascer nao ia lembrar de avisar. Um
/// laco que pergunta nao tem o que esquecer -- e o custo e uma duzia de consultas a dicionario por jogador.
///
/// ISTO NAO E PERMISSAO: um cliente mexido que acenda um bit ganha um botao que o servidor recusa.
/// ================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>De quantos em quantos tiques os fatos se conferem. 6 = cinco vezes por segundo (o passo da ficha).</summary>
	private const int TicksEntreFatos = 6;

	/// <summary>
	/// O que cada TELA recebeu por ultimo. A tela vai junto do valor porque id se reusa: o jogador que herda
	/// o id de quem saiu precisa receber os fatos DELE, mesmo que o numero seja igual ao do anterior.
	/// </summary>
	private readonly Dictionary<int, (NetPeer Tela, Fatos Fatos)> _fatosMandados = [];

	/// <summary>O que saiu no fio: (quem, fatos). Ligada so pela bancada.</summary>
	internal static List<(int Quem, Fatos Fatos)>? EscutaDeFatos;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--verbsteste`): os fatos nao saem quando mudam -- so o primeiro retrato de
	/// cada tela viaja. E o estado de quem ganha um convite e nunca ve o botao de aceitar. Falso em jogo, sempre.
	/// </summary>
	internal bool FatosCongeladosDeTeste;

	/// <summary>
	/// **OS FATOS DESTE CORPO, AGORA.** Cada linha e a MESMA pergunta que o verb correspondente faz ao recusar
	/// -- o arquivo e a linha estao ao lado, e a bancada `--verbsteste` cobra que as duas concordem.
	///
	/// Sem lambda e sem lista nova: isto roda pra cada jogador cinco vezes por segundo.
	/// </summary>
	internal Fatos FatosDe(ServerPlayer pl)
	{
		Fatos f = Fatos.Nenhum;
		Fighter ficha = pl.Ficha;
		long agora = NowMs();

		// ---- o que o corpo e ----
		if (ficha.AndroideAbsorcao) f |= Fatos.AndroideDeAbsorcao;          // `AlternarPosturaDeKi`, Androide.cs:96
		if (ficha.NeoTuffle && pl.CorpoDaPosse == null) f |= Fatos.NeoTuffle;   // `Possuir`, NeoTuffle.cs (2026-10-18): dentro de outro corpo, nao
		if (ficha.godki?.awakened == true) f |= Fatos.GodKiDesperto;        // `VerboDesafiar`, CargoDuelo.cs:206
		if (ficha.splitformCount > 0) f |= Fatos.TemCopias;                 // `OrdenarSplitformsG12`, Tecnicas.G12.cs:1603
		if (ficha.canSSJ) f |= Fatos.EscadaSuperSaiyajin;                   // `PerfilDeFormas.CanSsj`, Formas.cs:88
		if (TemArmaDeFogo(pl)) f |= Fatos.TemArmaDeFogo;                    // `ComandoDeArma`, Armas.cs
		if (PoeOvoAgora(pl)) f |= Fatos.PoeOvo;                             // `PorOvo`, Filhos.cs (2026-10-17)
		if (ficha.bio_lab_born) f |= Fatos.BioDeLaboratorio;               // `VirarLarva`, Larva.cs (2026-10-17)
		if (Jandirus.Core.Races.BioAndroids.EmLarva(ficha)) f |= Fatos.EmLarva;   // `AlternarEnterrar`, Larva.cs
		if (ficha.bio_lab_born && ficha.bio_nucleo.Length == 0) f |= Fatos.NucleoPorEscolher;   // `EscolherNucleo`, Nucleo.cs (2026-10-17)
		if (Jandirus.Core.Races.SkillsRaciais.AindaEscolhe(ficha) > 0) f |= Fatos.SkillDoDnaPorEscolher;   // `EscolherSkillDoDna`, SkillsDoDna.cs
		if (TemAVoltaDoJuiz(pl)) f |= Fatos.VoltaDoOutroMundo;              // `AtravessarOOutroMundo`, TravessiaDoJuiz.cs (2026-10-19)
		if (pl.UltraInstinct.Aprendida && !pl.PoderDaDestruicao.Aprendida && EhDeusDaDestruicao(pl))
			f |= Fatos.KitDaDestruicaoEmprestado;                           // `UsarOKitEmprestado`, KitDoDeus.cs
		if (Nutricao.VigorInfinitoDaRaca(ficha)) f |= Fatos.VigorInfinito;  // `ComprarNaMesa`, Customizadas.cs (2026-10-19): sem folego na mesa

		// ---- o que ele tem no mundo (tudo por ASSINATURA: e do personagem, nao da conta) ----
		string sig = pl.Assinatura;
		if (sig.Length > 0)
		{
			if (MestreDe(sig).Length > 0) f |= Fatos.TemMestre;             // `LargarOMestre`, Mestre.cs:399
			foreach (KeyValuePair<string, string> vinculo in _mestreDe)     // `PessoaNaFrente(soMeusAlunos)`, Mestre.cs:293
				if (string.Equals(vinculo.Value, sig, StringComparison.Ordinal)) { f |= Fatos.TemAluno; break; }

			foreach (Dominio d in _dominios)                                // `DominiosDe`, Conquista.cs:256
				if (string.Equals(d.Assinatura, sig, StringComparison.Ordinal)) { f |= Fatos.DominaAlgum; break; }
			if (MeuDominioAqui(pl) != null) f |= Fatos.DominaEstePlaneta;   // `ConquistaTributo`, Conquista.cs:771

			foreach (SetDeEsferas s in _sets)                               // `ReviverOSet`, Esferas.cs:1440-1442
				if (s.Zona.Hash == pl.Zone.Hash && string.Equals(s.CriadorSig, sig, StringComparison.Ordinal))
				{ f |= Fatos.CriouAEstatuaDaqui; break; }
			if (EstatuaSemCriadorAqui(pl.Zone)) f |= Fatos.EstatuaSemCriadorAqui;   // `ReviverOSet` pelo Cla do Dragao, VagaReciclada.cs (2026-10-19)

			if (TemAmorCorrespondido(pl)) f |= Fatos.TemUmAmor;            // `PedirFilho`, Filhos.cs (2026-10-17)
		}

		if (FusaoDe(pl.Id) is { } fusao && fusao.Dono == pl) f |= Fatos.DirigeUmaFusao;   // `PassarOControle`, Fusao.cs:2057-2058

		// ---- o que esta na mesa esperando resposta ----
		// (2026-10-13) AS CONDICOES MORAM NUM LUGAR SO, o `PedidosDe` (`GameServer.Pedidos.cs`): e dele que sai a lista
		// que a tela mostra como notificacao, e os fatos de pedido sao os tipos daquela lista. O PRAZO ENTRA la:
		// convite vencido nao e convite.
		PedidosDe(pl, _pedidosDosFatos);
		foreach (PedidoNaTela p in _pedidosDosFatos) f |= p.Fato;

		return f;
	}

	/// <summary>A lista reusada do `FatosDe` -- ele roda pra cada jogador cinco vezes por segundo.</summary>
	private readonly List<PedidoNaTela> _pedidosDosFatos = [];

	/// <summary>
	/// O TIQUE DOS FATOS: pra cada jogador com tela, o que e verdade agora -- e o pacote so se mudou.
	/// </summary>
	private void TickDosFatos()
	{
		if (VerbsDeTeste) DispararABancadaDosVerbs();   // a bancada `--verbsteste` (so com a flag) -- ver `GameServer.VerbsTeste.cs`

		foreach (ServerPlayer pl in _players.Values)
			if (pl.Peer != null && EhJogador(pl))
			{
				MandarFatos(pl);
				MandarPedidos(pl);   // ...e a notificacao de cada pedido pendente -- ver GameServer.Pedidos.cs
			}

		// A FAXINA: id de quem saiu nao fica na memoria. So roda quando sobra entrada, e sobra pouco.
		if (_fatosMandados.Count <= _players.Count) return;
		foreach (int id in _fatosMandados.Keys.ToList())
			if (!_players.ContainsKey(id)) { _fatosMandados.Remove(id); _pedidosMandados.Remove(id); }
	}

	/// <summary>Manda os fatos deste jogador se mudaram -- ou se esta TELA ainda nao recebeu nenhum.</summary>
	private void MandarFatos(ServerPlayer pl)
	{
		if (pl.Peer is not { } tela) return;

		Fatos f = FatosDe(pl);
		if (_fatosMandados.TryGetValue(pl.Id, out (NetPeer Tela, Fatos Fatos) antes) && ReferenceEquals(antes.Tela, tela)
			&& (antes.Fatos == f || FatosCongeladosDeTeste))
			return;
		_fatosMandados[pl.Id] = (tela, f);

		EscutaDeFatos?.Add((pl.Id, f));

		var w = Protocol.Begin(Protocol.S2C.Fatos);
		w.Put((ulong)f);   // (2026-10-19) `ulong`: o 33o fato (o aprendiz de Kaioshin)
		tela.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
