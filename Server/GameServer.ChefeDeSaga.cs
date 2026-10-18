using Godot;
using Jandirus.Core.Forms;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ O CHEFE DE SAGA NA TELA DE TODOS (v0.3.0, item 35 do pedido 15) ============================
/// O pedido: *"Seria bom uma cutscene q aparece pra todos quando um boss de saga aparece, e faça boss de saga de ki infinito
/// pra deixar mais desafiador. E n esqueça q quando boss se transoforma ele tem cinematica igual um player"*.
///
///   * A CHEGADA (<see cref="AnunciarChegadaDoChefe"/>): quando um chefe de saga chega -- ou volta da morte --, TODO jogador
///     no mundo, em qualquer planeta, ve a cena: o retrato dele, o nome, o planeta e a frase da chegada
///     (`S2C.ChegadaDoChefe`, desenhada pelo `Client/CenaDaChegada.cs`). Ela NAO prende ninguem: quem esta lutando do outro
///     lado da galaxia continua lutando -- a cena e o aviso grande de que algo chegou, e nao uma pausa do mundo inteiro por
///     causa de um chefe que nao esta la. Quem RETOMA (o reinicio do servidor com o chefe ja no mundo) nao e chegada, e nao
///     tem cena;
///   * O KI SEM FIM: o corpo do chefe nasce com `Fighter.KiDeChefe` (`NascerChefeDaSaga`), e o cadeado do corpo o segura no
///     cheio no fim de todo tique (`Nutricao.KiSempreCheio`) -- o raio que ele segura, a carga e a forma nunca o esvaziam;
///   * A TRANSFORMACAO (<see cref="CenaDoChefe"/>): o degrau que troca o CORPO (o Freeza) ou que muda sem trocar nada visivel
///     (o Cell) toca a cena que o `npcs.json` nomeia -- as cenas de JOGADOR (`Cinematicas.DoChefe`). O degrau de FORMA ja
///     passava pelo funil do jogador (`AnunciarForma`). Preso e intocavel enquanto ela dura, como o jogador na dele
///     (`CombatState.Intocavel` pelo `CenaSegundos`), e o corpo novo so aparece na virada (`World._pendentes`).
/// ==========================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>A cena da chegada que saiu: quem chegou, onde, a frase e pra quantos jogadores foi. So bancada; nula em jogo.</summary>
	internal static List<(string Nome, string Planeta, string Frase, int Para)>? EscutaDeChegadas;

	/// <summary>DEFEITO INJETADO (`--v030teste`, `--diagv030`): a chegada so vai pro chat -- o jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool ChegadaSemCenaDeTeste;

	/// <summary>
	/// A CENA DA CHEGADA, pra todo jogador do mundo: o id do corpo, o nome, o planeta (o nome bonito), a frase da chegada e o
	/// que desenha o retrato -- a raca, o genero e a aparencia inteira (o mesmo boneco em camadas do mundo).
	/// </summary>
	/// <param name="quemVe">(2026-10-16) Quem ve a cena. Nulo = todo jogador (o chefe de saga); o vilao que volta no Inferno a mostra so
	/// a quem esta no Outro Mundo (`GameServer.ViloesDoInferno`).</param>
	private void AnunciarChegadaDoChefe(ServerPlayer chefe, string planeta, string frase, Func<ServerPlayer, bool>? quemVe = null)
	{
		if (ChegadaSemCenaDeTeste) return;
		var w = Protocol.Begin(Protocol.S2C.ChegadaDoChefe);
		w.Put(chefe.Id);
		w.Put(chefe.Name);
		w.Put(NomeDoPlaneta(planeta));
		w.Put(frase);
		w.Put(chefe.Race);
		w.Put(chefe.Genero);
		w.PutAppearance(chefe.Visual);

		int para = 0;
		foreach (ServerPlayer o in Jogadores)
		{
			if (o.Peer == null || (quemVe != null && !quemVe(o))) continue;
			o.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			para++;
		}
		EscutaDeChegadas?.Add((chefe.Name, planeta, frase, para));
	}

	/// <summary>A cena de degrau que saiu: o corpo e qual. So bancada; nula em jogo.</summary>
	internal static List<(int Quem, Cinematicas.CenaDeChefe Cena)>? EscutaDeCenasDeChefe;

	/// <summary>DEFEITO INJETADO (`--v030teste`, `--diagv030`): o chefe troca de corpo sem cena -- o jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool ChefeSemCenaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--v030teste`): o degrau de FORMA de chefe deixa de ser a estreia -- o jogo de antes, em que a maestria do
	/// molde (60% no guardiao) dispensava a cena. Lido pelo `EntrarNoDegrau`. Falso em jogo, sempre.
	/// </summary>
	internal static bool FormaDeChefeSemEstreiaDeTeste;

	/// <summary>
	/// A CENA DE UM DEGRAU DE CHEFE, pra quem esta na zona. O prazo PRIMEIRO, como no `CenaDoBio`: quem receber o pacote
	/// comeca a contar no quadro seguinte, e o portao do servidor (preso e intocavel) ja tem que estar fechado. Chamada ANTES
	/// da aparencia nova, no mesmo canal ordenado -- e assim que o corpo novo espera a virada no cliente.
	/// </summary>
	private void CenaDoChefe(ServerPlayer chefe, Cinematicas.CenaDeChefe qual)
	{
		if (ChefeSemCenaDeTeste || Cinematicas.DoChefe(qual) is not { } cena) return;
		chefe.CenaSegundos = cena.SegundosPreso;

		var w = Protocol.Begin(Protocol.S2C.CenaDoChefe);
		w.Put(chefe.Id);
		w.Put((byte)qual);
		foreach (ServerPlayer o in ZoneList(chefe.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		EscutaDeCenasDeChefe?.Add((chefe.Id, qual));
		GD.Print($"[server] {chefe.Name}: CENA DO CHEFE '{qual}' ({chefe.CenaSegundos:0.#}s preso)");
	}
}
